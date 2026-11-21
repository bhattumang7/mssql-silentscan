using System.Data;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;

namespace SilentScan.Verify.Oracle;

public enum EngineVerdict
{
    Confirmed,

    Refuted,

    NotDecided,
}

public sealed record EngineVerification
{
    internal EngineVerification(EngineVerdict verdict, string detail, bool engineRefusal = false)
    {
        Verdict = verdict;
        Detail = detail;
        EngineRefusal = engineRefusal;
    }

    public EngineVerdict Verdict { get; }

    public string Detail { get; }

    public bool EngineRefusal { get; }
}

public enum Difference
{
    ResultEmptied,

    ResultChanged,

    NullAppeared,

    FailureSuppressed,
}

public abstract record Touch;

public sealed record TouchedObject(string? Schema, string Name) : Touch;

public sealed record TouchedExpression(string Text) : Touch;

public delegate Task<int> PerturbationStep(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken);

public sealed class Perturbation
{
    public Perturbation(
        IReadOnlyList<Touch> touches,
        Difference expected,
        PerturbationStep removeCondition,
        PerturbationStep applyCondition,
        Func<SqlException, bool> engineRefusalRefutes,
        Func<string, string?>? baselineStatement = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(touches.Count);
        BaselineStatement = baselineStatement;
        Touches = touches;
        Expected = expected;
        RemoveCondition = removeCondition;
        ApplyCondition = applyCondition;
        EngineRefusalRefutes = engineRefusalRefutes;
    }

    private Perturbation(string notApplicableReason)
    {
        Touches = [];
        RemoveCondition = static (_, _, _) => Task.FromResult(0);
        ApplyCondition = static (_, _, _) => Task.FromResult(0);
        EngineRefusalRefutes = static _ => false;
        NotApplicableReason = notApplicableReason;
    }

    public IReadOnlyList<Touch> Touches { get; }

    public Difference Expected { get; }

    public PerturbationStep RemoveCondition { get; }

    public PerturbationStep ApplyCondition { get; }

    public Func<SqlException, bool> EngineRefusalRefutes { get; }

    public Func<string, string?>? BaselineStatement { get; }

    public string? NotApplicableReason { get; }

    public static Perturbation NotApplicable(string reason) => new(reason);
}

public interface IRuleVerifier
{
    string RuleId { get; }

    Type FindingType { get; }

    bool Judges(IFinding finding);

    Task<EngineVerification> VerifyAsync(
        SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken = default);
}

public abstract class RuleVerifier<TFinding> : IRuleVerifier
    where TFinding : class, IFinding
{
    private const string NullCell = "<null>";

    private const string StatementSavepoint = "silentscan_judged_statement";

    public abstract string RuleId { get; }

    public Type FindingType => typeof(TFinding);

    protected abstract Perturbation Describe(TFinding finding);

    protected virtual string RuleIdOf(TFinding finding) =>
        typeof(TFinding).GetProperty("RuleId")?.GetValue(finding) as string
        ?? throw new InvalidOperationException($"{typeof(TFinding).Name} has no RuleId property; {GetType().Name} must override RuleIdOf.");

    public bool Judges(IFinding finding) =>
        finding is TFinding typed && string.Equals(RuleIdOf(typed), RuleId, StringComparison.Ordinal);

    public async Task<EngineVerification> VerifyAsync(
        SqlServerOptions options, string database, TFinding finding, string statement, CancellationToken cancellationToken = default)
    {
        var perturbation = Describe(finding);
        if (perturbation.NotApplicableReason is { } reason)
        {
            return NotDecided(reason);
        }

        await using var connection = new SqlConnection(options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await HarnessDatabaseGuard.RequireAsync(connection, cancellationToken);

        try
        {
            if (UnreadTouch(statement, perturbation.Touches) is { } missing)
            {
                return NotDecided($"The statement does not contain exactly one {missing}, so the claim cannot be observed through it.");
            }

            var objects = perturbation.Touches.OfType<TouchedObject>().ToList();
            if (objects.Count > 0 && !await PlanReadsEveryObjectAsync(connection, PlanText(statement), objects, cancellationToken))
            {
                return NotDecided($"The statement's plan does not read {string.Join(", ", objects.Select(NameOf))}, so the claim cannot be observed through it.");
            }

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                return await JudgeAsync(connection, transaction, statement, perturbation, cancellationToken);
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }
        catch (SqlException ex)
        {
            return NotDecided($"The judged statement or its setup could not run: {ex.Message}");
        }
    }

    Task<EngineVerification> IRuleVerifier.VerifyAsync(
        SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken) =>
        VerifyAsync(options, database, (TFinding)finding, statement, cancellationToken);

    private static async Task<EngineVerification> JudgeAsync(
        SqlConnection connection, SqlTransaction transaction, string statement, Perturbation perturbation, CancellationToken cancellationToken)
    {
        _ = await perturbation.RemoveCondition(connection, transaction, cancellationToken);

        var baselineStatement = perturbation.BaselineStatement is { } rewrite ? rewrite(statement) : statement;
        if (baselineStatement is null)
        {
            return NotDecided("The statement has no single place where the condition can be removed, so there is nothing to compare it with.");
        }

        if (perturbation.BaselineStatement is not null && string.Equals(baselineStatement, statement, StringComparison.Ordinal))
        {
            return NotDecided("The rewrite left the statement as it was, so the condition was never removed.");
        }

        var allowFailure = perturbation.Expected == Difference.FailureSuppressed;
        var baseline = await ObserveOnceAsync(connection, transaction, baselineStatement, allowFailure, cancellationToken);
        var control = await ObserveOnceAsync(connection, transaction, baselineStatement, allowFailure, cancellationToken);
        if (!baseline.SameAs(control))
        {
            return NotDecided("The statement returns different results on its own, with nothing changed, so a difference cannot be credited to the condition.");
        }

        int changed;
        try
        {
            changed = await perturbation.ApplyCondition(connection, transaction, cancellationToken);
        }
        catch (SqlException ex) when (perturbation.EngineRefusalRefutes(ex))
        {
            return new EngineVerification(EngineVerdict.Refuted, $"The engine refused the condition the claim depends on: {ex.Message}", engineRefusal: true);
        }

        if (changed == 0 && perturbation.BaselineStatement is null)
        {
            return NotDecided("The condition changed no rows, so nothing was put in place to observe.");
        }

        var perturbed = await ObserveOnceAsync(connection, transaction, statement, allowFailure, cancellationToken);
        return Classify(perturbation.Expected, baseline, perturbed);
    }

    private static EngineVerification Classify(Difference expected, Observation baseline, Observation perturbed)
    {
        if (expected == Difference.FailureSuppressed)
        {
            return ClassifyFailure(baseline, perturbed);
        }

        if (expected is Difference.ResultEmptied or Difference.NullAppeared && baseline.IsEmpty)
        {
            return NotDecided("The statement returns nothing even before the condition, so there is nothing for the condition to act on.");
        }

        if (expected == Difference.NullAppeared)
        {
            return perturbed.NullCells > baseline.NullCells
                ? new EngineVerification(EngineVerdict.Confirmed, "Applying the condition turned a value the statement returns into NULL.")
                : new EngineVerification(EngineVerdict.Refuted, "Applying the condition did not turn any value the statement returns into NULL.");
        }

        if (perturbed.SameAs(baseline))
        {
            return new EngineVerification(EngineVerdict.Refuted, "Applying the condition did not change the statement's result.");
        }

        if (expected == Difference.ResultChanged)
        {
            return new EngineVerification(EngineVerdict.Confirmed, "Applying the condition changed the statement's result.");
        }

        return perturbed.IsEmpty
            ? new EngineVerification(
                EngineVerdict.Confirmed,
                string.Create(CultureInfo.InvariantCulture, $"Applying the condition emptied the statement's result ({baseline.Rows.Count} row(s) and {baseline.RecordsAffected} affected before)."))
            : NotDecided("The condition changed the statement's result, but did not empty it as the claim describes.");
    }

    private static EngineVerification ClassifyFailure(Observation baseline, Observation perturbed)
    {
        if (baseline.FailureNumber is null)
        {
            return perturbed.FailureNumber is null
                ? new EngineVerification(EngineVerdict.Refuted, "The statement does not fail even without the condition, so nothing is being silently skipped.")
                : NotDecided("The statement succeeds without the condition but fails with it, which is the opposite of what the claim describes.");
        }

        return perturbed.FailureNumber is null
            ? new EngineVerification(EngineVerdict.Confirmed, string.Create(CultureInfo.InvariantCulture, $"Without the condition the statement fails with error {baseline.FailureNumber}; with it the statement succeeds silently."))
            : new EngineVerification(EngineVerdict.Refuted, string.Create(CultureInfo.InvariantCulture, $"The statement still fails with error {perturbed.FailureNumber} when the condition is applied."));
    }

    private static async Task<Observation> ObserveOnceAsync(
        SqlConnection connection, SqlTransaction transaction, string statement, bool allowFailure, CancellationToken cancellationToken)
    {
        transaction.Save(StatementSavepoint);
        try
        {
            await using var command = new SqlCommand(statement, connection, transaction) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<string>();
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var values = new object?[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Add(string.Join('|', values.Select(v => v is DBNull ? NullCell : Convert.ToString(v, CultureInfo.InvariantCulture))));
                }
            }
            while (await reader.NextResultAsync(cancellationToken));

            return new Observation(rows, reader.RecordsAffected);
        }
        catch (SqlException ex) when (allowFailure)
        {
            return new Observation([], -1, ex.Number);
        }
        finally
        {
            transaction.Rollback(StatementSavepoint);
        }
    }

    private static async Task<bool> PlanReadsEveryObjectAsync(
        SqlConnection connection, string statement, IReadOnlyList<TouchedObject> touches, CancellationToken cancellationToken)
    {
        await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
        {
            await on.ExecuteNonQueryAsync(cancellationToken);
        }

        string? planXml = null;
        try
        {
            await using var command = new SqlCommand(statement, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string) && reader.GetString(0) is { } value
                        && value.Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        planXml = value;
                    }
                }
            }
            while (await reader.NextResultAsync(cancellationToken));
        }
        finally
        {
            await using var off = new SqlCommand("SET SHOWPLAN_XML OFF;", connection);
            await off.ExecuteNonQueryAsync(CancellationToken.None);
        }

        if (planXml is null)
        {
            return false;
        }

        var objects = XDocument.Parse(planXml).Descendants().Where(e => e.Name.LocalName == "Object").ToList();
        return touches.All(t => objects.Exists(e =>
            string.Equals(Unbracket((string?)e.Attribute("Table")), t.Name, StringComparison.OrdinalIgnoreCase)
            && (t.Schema is null || string.Equals(Unbracket((string?)e.Attribute("Schema")), t.Schema, StringComparison.OrdinalIgnoreCase))));
    }

    private static string? Unbracket(string? name) => name?.Trim('[', ']');

    private static string? UnreadTouch(string statement, IReadOnlyList<Touch> touches)
    {
        foreach (var expression in touches.OfType<TouchedExpression>())
        {
            var first = statement.IndexOf(expression.Text, StringComparison.OrdinalIgnoreCase);
            if (first < 0 || statement.IndexOf(expression.Text, first + 1, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"expression '{expression.Text}'";
            }
        }

        return null;
    }

    private static string PlanText(string statement)
    {
        const string Prefix = "EXEC sp_executesql N'";
        var trimmed = statement.Trim().TrimEnd(';');
        return trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && trimmed[^1] == (char)39
            ? trimmed.Substring(Prefix.Length, trimmed.Length - Prefix.Length - 1).Replace("''", "'", StringComparison.Ordinal)
            : statement;
    }

    private static string NameOf(TouchedObject touched) => touched.Schema is null ? touched.Name : touched.Schema + "." + touched.Name;

    private static EngineVerification NotDecided(string detail) => new(EngineVerdict.NotDecided, detail);

    private sealed record Observation(List<string> Rows, int RecordsAffected, int? FailureNumber = null)
    {
        public int NullCells => Rows.Sum(row => row.Split('|').Count(cell => string.Equals(cell, NullCell, StringComparison.Ordinal)));

        public bool IsEmpty => Rows.Count == 0 && RecordsAffected <= 0;

        public bool SameAs(Observation other) =>
            RecordsAffected == other.RecordsAffected && FailureNumber == other.FailureNumber && Rows.SequenceEqual(other.Rows, StringComparer.Ordinal);
    }
}

public static class RuleVerifierRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, IRuleVerifier>> Verifiers = new(Discover);

    public static IReadOnlyDictionary<string, IRuleVerifier> All => Verifiers.Value;

    public static IRuleVerifier? For(string ruleId) => All.TryGetValue(ruleId, out var verifier) ? verifier : null;

    private static Dictionary<string, IRuleVerifier> Discover()
    {
        var verifiers = new Dictionary<string, IRuleVerifier>(StringComparer.Ordinal);
        var candidates = typeof(IRuleVerifier).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IRuleVerifier).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
        foreach (var type in candidates)
        {
            var verifier = (IRuleVerifier)Activator.CreateInstance(type)!;
            if (!verifiers.TryAdd(verifier.RuleId, verifier))
            {
                throw new InvalidOperationException($"Rule {verifier.RuleId} has more than one verifier: {verifiers[verifier.RuleId].GetType().Name} and {type.Name}.");
            }
        }

        return verifiers;
    }
}

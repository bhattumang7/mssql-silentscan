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
    internal EngineVerification(EngineVerdict verdict, string detail)
    {
        Verdict = verdict;
        Detail = detail;
    }

    public EngineVerdict Verdict { get; }

    public string Detail { get; }
}

public enum Difference
{
    ResultEmptied,

    ResultChanged,
}

public sealed record TouchedObject(string? Schema, string Name);

public delegate Task PerturbationStep(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken);

public sealed class Perturbation
{
    public Perturbation(
        IReadOnlyList<TouchedObject> touches,
        Difference expected,
        PerturbationStep removeCondition,
        PerturbationStep applyCondition,
        Func<SqlException, bool> engineRefusalRefutes)
    {
        Touches = touches;
        Expected = expected;
        RemoveCondition = removeCondition;
        ApplyCondition = applyCondition;
        EngineRefusalRefutes = engineRefusalRefutes;
    }

    private Perturbation(string notApplicableReason)
    {
        Touches = [];
        RemoveCondition = static (_, _, _) => Task.CompletedTask;
        ApplyCondition = static (_, _, _) => Task.CompletedTask;
        EngineRefusalRefutes = static _ => false;
        NotApplicableReason = notApplicableReason;
    }

    public IReadOnlyList<TouchedObject> Touches { get; }

    public Difference Expected { get; }

    public PerturbationStep RemoveCondition { get; }

    public PerturbationStep ApplyCondition { get; }

    public Func<SqlException, bool> EngineRefusalRefutes { get; }

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
            if (!await PlanReadsEveryObjectAsync(connection, statement, perturbation.Touches, cancellationToken))
            {
                return NotDecided($"The statement's plan does not read {string.Join(", ", perturbation.Touches.Select(NameOf))}, so the claim cannot be observed through it.");
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
        await perturbation.RemoveCondition(connection, transaction, cancellationToken);

        var baseline = await ObserveOnceAsync(connection, transaction, statement, cancellationToken);
        var control = await ObserveOnceAsync(connection, transaction, statement, cancellationToken);
        if (!baseline.SameAs(control))
        {
            return NotDecided("The statement returns different results on its own, with nothing changed, so a difference cannot be credited to the condition.");
        }

        try
        {
            await perturbation.ApplyCondition(connection, transaction, cancellationToken);
        }
        catch (SqlException ex) when (perturbation.EngineRefusalRefutes(ex))
        {
            return new EngineVerification(EngineVerdict.Refuted, $"The engine refused the condition the claim depends on: {ex.Message}");
        }

        var perturbed = await ObserveOnceAsync(connection, transaction, statement, cancellationToken);
        return Classify(perturbation.Expected, baseline, perturbed);
    }

    private static EngineVerification Classify(Difference expected, Observation baseline, Observation perturbed)
    {
        if (expected == Difference.ResultEmptied && baseline.IsEmpty)
        {
            return NotDecided("The statement returns nothing even before the condition, so there is no result for it to empty.");
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

    private static async Task<Observation> ObserveOnceAsync(
        SqlConnection connection, SqlTransaction transaction, string statement, CancellationToken cancellationToken)
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
                    rows.Add(string.Join('|', values.Select(v => v is DBNull ? "<null>" : Convert.ToString(v, CultureInfo.InvariantCulture))));
                }
            }
            while (await reader.NextResultAsync(cancellationToken));

            return new Observation(rows, reader.RecordsAffected);
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

    private static string NameOf(TouchedObject touched) => touched.Schema is null ? touched.Name : touched.Schema + "." + touched.Name;

    private static EngineVerification NotDecided(string detail) => new(EngineVerdict.NotDecided, detail);

    private sealed record Observation(List<string> Rows, int RecordsAffected)
    {
        public bool IsEmpty => Rows.Count == 0 && RecordsAffected <= 0;

        public bool SameAs(Observation other) =>
            RecordsAffected == other.RecordsAffected && Rows.SequenceEqual(other.Rows, StringComparer.Ordinal);
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

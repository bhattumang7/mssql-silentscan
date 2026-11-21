using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;

namespace SilentScan.Verify.Oracle;

public enum EngineVerdict
{
    Confirmed,

    Refuted,

    NotDecided,
}

public sealed record EngineVerification(EngineVerdict Verdict, string Detail);

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
    public abstract string RuleId { get; }

    public Type FindingType => typeof(TFinding);

    protected abstract Task<EngineVerification> ObserveAsync(
        SqlConnection connection, TFinding finding, string statement, CancellationToken cancellationToken);

    protected virtual string RuleIdOf(TFinding finding) =>
        typeof(TFinding).GetProperty("RuleId")?.GetValue(finding) as string
        ?? throw new InvalidOperationException($"{typeof(TFinding).Name} has no RuleId property; {GetType().Name} must override RuleIdOf.");

    public bool Judges(IFinding finding) =>
        finding is TFinding typed && string.Equals(RuleIdOf(typed), RuleId, StringComparison.Ordinal);

    public async Task<EngineVerification> VerifyAsync(
        SqlServerOptions options, string database, TFinding finding, string statement, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        return await ObserveAsync(connection, finding, statement, cancellationToken);
    }

    Task<EngineVerification> IRuleVerifier.VerifyAsync(
        SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken) =>
        VerifyAsync(options, database, (TFinding)finding, statement, cancellationToken);
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

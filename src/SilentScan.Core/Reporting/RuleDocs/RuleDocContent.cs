namespace SilentScan.Core.Reporting.RuleDocs;

public sealed record RuleDocBehaviorProof(string SetupSql, string QuerySql, string? ExpectedScalar);

public sealed record RuleDocExample(
    string Title,
    string NoncompliantSql,
    string? NoncompliantExplanation = null,
    string? CompliantSql = null,
    string? CompliantExplanation = null,
    RuleDocBehaviorProof? NoncompliantProof = null,
    RuleDocBehaviorProof? CompliantProof = null,
    bool RequiresLatestEngine = false);

public sealed record RuleDocContent(string WhyItMatters, string? HowToFixIt = null, IReadOnlyList<RuleDocExample>? Examples = null)
{
    public IReadOnlyList<RuleDocExample> AllExamples => Examples ?? [];
}

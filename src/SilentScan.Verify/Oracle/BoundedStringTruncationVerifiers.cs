using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
public abstract partial class BoundedStringTruncationVerifier : RuleVerifier<BoundedStringBuiltinTruncationFinding>
{
    protected abstract string? Uncapped(string statement);

    protected override Perturbation Describe(BoundedStringBuiltinTruncationFinding finding) =>
        new(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.CompletedTask,
            static (_, _, _) => Task.CompletedTask,
            static _ => false,
            Uncapped);

    protected static string? WidenFirstLiteral(string statement, Regex call)
    {
        var count = call.Count(statement);
        return count switch
        {
            0 => statement,
            1 => call.Replace(statement, static m => m.Groups["head"].Value + "CAST(" + m.Groups["literal"].Value
                + (m.Groups["national"].Success ? " AS NVARCHAR(MAX))" : " AS VARCHAR(MAX))")),
            _ => null,
        };
    }

    [GeneratedRegex(@"(?<head>\bREPLICATE\s*\(\s*)(?<national>N)?(?<literal>'(?:[^']|'')*')", RegexOptions.IgnoreCase)]
    protected static partial Regex ReplicateWithLiteralSource();

    [GeneratedRegex(@"(?<head>\bREPLACE\s*\(\s*)(?<national>N)?(?<literal>'(?:[^']|'')*')", RegexOptions.IgnoreCase)]
    protected static partial Regex ReplaceWithLiteralInput();

    [GeneratedRegex(@"\bSPACE\s*\(\s*(?<count>[^()]+?)\s*\)", RegexOptions.IgnoreCase)]
    protected static partial Regex SpaceCall();
}

public sealed class ReplicateTruncationVerifier : BoundedStringTruncationVerifier
{
    public override string RuleId => FindingRuleIds.BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId;

    protected override string? Uncapped(string statement) => WidenFirstLiteral(statement, ReplicateWithLiteralSource());
}

public sealed class ReplaceTruncationVerifier : BoundedStringTruncationVerifier
{
    public override string RuleId => FindingRuleIds.BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId;

    protected override string? Uncapped(string statement) => WidenFirstLiteral(statement, ReplaceWithLiteralInput());
}

public sealed class SpaceTruncationVerifier : BoundedStringTruncationVerifier
{
    public override string RuleId => FindingRuleIds.BoundedStringBuiltinTruncationSpaceResultTruncatedRuleId;

    protected override string? Uncapped(string statement) => SpaceCall().Count(statement) switch
    {
        0 => statement,
        1 => SpaceCall().Replace(statement, static m => "REPLICATE(CAST(' ' AS VARCHAR(MAX)), " + m.Groups["count"].Value + ")"),
        _ => null,
    };
}

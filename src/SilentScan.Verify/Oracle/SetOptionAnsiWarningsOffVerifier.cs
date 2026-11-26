using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SetOptionAnsiWarningsOffVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId;

    [GeneratedRegex(@"SET\s+ANSI_WARNINGS\s+OFF", RegexOptions.IgnoreCase)]
    private static partial Regex Setting();

    protected override Perturbation Describe(SetOptionFinding finding) =>
        SetOptionRestoreProbe.Describe(finding.ModuleQualifiedName, Setting(), "SET ANSI_WARNINGS ON");
}

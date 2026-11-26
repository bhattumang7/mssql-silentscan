using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SetOptionNumericRoundabortOnVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId;

    [GeneratedRegex(@"SET\s+NUMERIC_ROUNDABORT\s+ON", RegexOptions.IgnoreCase)]
    private static partial Regex Setting();

    protected override Perturbation Describe(SetOptionFinding finding) =>
        SetOptionRestoreProbe.Describe(finding.ModuleQualifiedName, Setting(), "SET NUMERIC_ROUNDABORT OFF");
}

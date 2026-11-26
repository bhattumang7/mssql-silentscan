using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SetOptionConcatNullYieldsNullOffVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId;

    [GeneratedRegex(@"SET\s+CONCAT_NULL_YIELDS_NULL\s+OFF", RegexOptions.IgnoreCase)]
    private static partial Regex Setting();

    protected override Perturbation Describe(SetOptionFinding finding) =>
        SetOptionRestoreProbe.Describe(finding.ModuleQualifiedName, Setting(), "SET CONCAT_NULL_YIELDS_NULL ON");
}

using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Rules;

public static class ScalarUdfClassifier
{
    public static ScalarUdfFindingKind ClassifyInvocationKind(ScalarUdfContext context) =>
        context.IsPredicate() ? ScalarUdfFindingKind.PredicateInvocation : ScalarUdfFindingKind.ProjectionInvocation;

    public static bool CannotBeFoldedToOneEvaluation(ScalarUdfInfo info) =>
        info.IsSchemaBound == false
        || info.InlineabilityTableReferenceCount > 0
        || (info.InlineabilityBlocker?.StartsWith(TimeDependentBlockerPrefix, StringComparison.Ordinal) ?? false);

    private const string TimeDependentBlockerPrefix = "time-dependent intrinsic";
}

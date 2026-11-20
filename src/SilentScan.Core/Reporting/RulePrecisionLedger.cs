using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting;

public sealed record RulePrecisionEntry(string RuleId, int HighFindings, int TruePositives, int FalsePositives, int Undecided)
{
    public const double RequiredPrecision = 0.95;

    public const int MinimumDecided = 5;

    public int Decided => TruePositives + FalsePositives;

    public double Precision => Decided == 0 ? 0 : (double)TruePositives / Decided;

    public bool IsDefaultOn =>
        Decided > 0
        && Precision >= RequiredPrecision
        && (Decided >= MinimumDecided || Decided == HighFindings);
}

public static class RulePrecisionLedger
{
    public static IReadOnlyList<RulePrecisionEntry> Entries { get; } =
    [
        new("silentscan/call-graph/argument-type-mismatch", 92, 4, 3, 13),
        new("silentscan/call-graph/sp-executesql-parameter-type-mismatch", 1, 1, 0, 0),
        new("silentscan/catalog/check-constraint-null-not-handled", 68, 0, 0, 20),
        new("silentscan/catalog/max-typed-column", 242, 0, 16, 4),
        new("silentscan/catalog/non-persisted-computed-column", 41, 1, 5, 14),
        new("silentscan/catalog/tvf-return-database-collation", 27, 11, 0, 9),
        new("silentscan/catalog/untrusted-check-constraint", 16, 16, 0, 0),
        new("silentscan/catalog/with-recompile", 2, 2, 0, 0),
        new("silentscan/control-flow/case-expression-missing-else", 57, 1, 12, 7),
        new("silentscan/control-flow/unassigned-output-parameter", 627, 0, 0, 20),
        new("silentscan/control-flow/unresolved-transaction", 353, 0, 0, 20),
        new("silentscan/correctness/nonunique-update-source", 3, 0, 1, 2),
        new("silentscan/correctness/not-in-nullable-subquery", 5, 5, 0, 0),
        new("silentscan/correctness/not-null-predicate-contradiction", 2, 2, 0, 0),
        new("silentscan/deprecated-syntax/equals-null-comparison", 9, 9, 0, 0),
        new("silentscan/deprecated-syntax/not-equals-null-comparison", 1, 1, 0, 0),
        new("silentscan/dml/self-referencing", 362, 4, 0, 16),
        new("silentscan/dynamic-sql/concatenated-value-in-constant-sql", 1, 0, 1, 0),
        new("silentscan/dynamic-sql/exec-string-concatenates-parameterizable-value", 1, 0, 1, 0),
        new("silentscan/forced-serial/fast-forward-cursor", 155, 2, 8, 10),
        new("silentscan/forced-serial/nonparallelizable-intrinsic", 5, 5, 0, 0),
        new("silentscan/forced-serial/table-variable-modification", 1361, 18, 1, 1),
        new("silentscan/hint/index-not-seekable", 1, 1, 0, 0),
        new("silentscan/index-design/duplicate-index", 1, 1, 0, 0),
        new("silentscan/index-design/filter-column-not-in-index", 9, 0, 9, 0),
        new("silentscan/index-design/float-or-real-index-key-column", 1, 0, 0, 1),
        new("silentscan/index-design/heap-with-nonclustered-indexes", 88, 0, 12, 8),
        new("silentscan/index-design/subsumed-index", 2, 2, 0, 0),
        new("silentscan/index-design/unindexed-foreign-key", 856, 0, 0, 20),
        new("silentscan/index-shape/composite-leading-column-unconstrained", 898, 0, 2, 18),
        new("silentscan/index/key-lookup-prone", 599, 4, 7, 9),
        new("silentscan/join/cartesian-comma-join", 1, 0, 1, 0),
        new("silentscan/join/outer-join-predicate-collapse", 87, 15, 0, 5),
        new("silentscan/lineage/expression-derived-column", 5421, 1, 18, 1),
        new("silentscan/lineage/multi-referenced-cte", 151, 17, 2, 1),
        new("silentscan/lineage/post-expansion-join-width", 2985, 0, 17, 3),
        new("silentscan/lineage/select-star-view", 1, 0, 0, 1),
        new("silentscan/predicate/plus-operator-null-propagation", 39, 12, 8, 0),
        new("silentscan/predicates/catch-all-parameter", 13, 1, 12, 0),
        new("silentscan/predicates/float-equality", 29, 0, 2, 18),
        new("silentscan/predicates/float-order-dependent-aggregate", 3, 3, 0, 0),
        new("silentscan/predicates/isnull-replacement-value-truncation", 11, 10, 1, 0),
        new("silentscan/predicates/under-length-parameter", 39, 16, 3, 1),
        new("silentscan/query/count-star-variable-existence-check", 8, 2, 0, 6),
        new("silentscan/query/table-variable-low-compat-estimate", 1615, 17, 1, 2),
        new("silentscan/scalar-udf/in-computed-column-or-constraint", 45, 4, 0, 16),
        new("silentscan/scalar-udf/in-predicate", 612, 20, 0, 0),
        new("silentscan/scalar-udf/in-select-or-expression", 1820, 11, 7, 2),
        new("silentscan/scalar-udf/nested-under-view-or-tvf", 1234, 0, 0, 20),
        new("silentscan/tier1/case-fold-on-column", 6, 4, 2, 0),
        new("silentscan/tier1/cast-or-convert-on-column", 22, 0, 20, 0),
        new("silentscan/tier1/charindex-or-left-on-column", 2, 2, 0, 0),
        new("silentscan/tier1/column-arithmetic", 125, 0, 20, 0),
        new("silentscan/tier1/date-function-on-column", 306, 7, 11, 2),
        new("silentscan/tier1/function-wrapped-column", 466, 1, 18, 1),
        new("silentscan/tier1/leading-wildcard-like", 2, 2, 0, 0),
        new("silentscan/trigger/multi-row-unsafe-single-row-assignment", 1, 1, 0, 0),
        new("silentscan/trigger/update-function-without-value-comparison", 13, 4, 5, 4),
        new("silentscan/tvf-fence/correlated-apply", 92, 15, 5, 0),
        new("silentscan/tvf-fence/nested-under-view-or-tvf", 11, 2, 9, 0),
        new("silentscan/verdict/range-seek", 5, 0, 0, 5),
        new("silentscan/verdict/scan-forced", 252, 16, 3, 1),
        new("silentscan/view/top-percent-order-by-no-op", 3, 3, 0, 0),
        new("silentscan/window-frame/implicit-default-range", 2, 2, 0, 0),
        new("silentscan/write-loss/approximate-to-exact-truncation", 34, 19, 0, 1),
        new("silentscan/write-loss/length-truncation", 1226, 2, 2, 16),
        new("silentscan/write-loss/numeric-scale-narrowing", 244, 19, 0, 1),
        new("silentscan/write-loss/temporal-precision-loss", 111, 1, 0, 19),
        new("silentscan/write-loss/unicode-to-non-unicode", 421, 1, 13, 6),
    ];

    private static readonly HashSet<string> DefaultOn =
        [.. Entries.Where(e => e.IsDefaultOn).Select(e => e.RuleId)];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> RuleIdProperties = new();

    public static bool IsDefaultOn(string ruleId) => DefaultOn.Contains(ruleId);

    public static ScanReport ApplyDefaultVisibility(ScanReport report, FindingConfidence minimumConfidence)
    {
        if (minimumConfidence != FindingConfidence.High)
        {
            return report;
        }

        var visible = new Dictionary<string, IReadOnlyList<IFinding>>(StringComparer.Ordinal);
        foreach (var (key, findings) in report.FindingsByRuleId)
        {
            visible[key] = [.. findings.Where(IsVisibleByDefault)];
        }

        return report with { FindingsByRuleId = visible };
    }

    private static bool IsVisibleByDefault(IFinding finding)
    {
        var property = RuleIdProperties.GetOrAdd(finding.GetType(), type => type.GetProperty("RuleId", typeof(string)));
        return property?.GetValue(finding) is not string ruleId || IsDefaultOn(ruleId);
    }
}

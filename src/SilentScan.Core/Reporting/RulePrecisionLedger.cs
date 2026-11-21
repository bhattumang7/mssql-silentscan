using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting;

public sealed record RulePrecisionEntry(string RuleId, int HighFindings, int TruePositives, int FalsePositives, int Undecided)
{
    public int Decided => TruePositives + FalsePositives;

    public double Precision => Decided == 0 ? 0 : (double)TruePositives / Decided;

}

public static class RulePrecisionLedger
{
    public static IReadOnlyList<RulePrecisionEntry> Entries { get; } =
    [
        new("silentscan/call-graph/argument-type-mismatch", 92, 11, 9, 0),
        new("silentscan/call-graph/sp-executesql-parameter-type-mismatch", 1, 1, 0, 0),
        new("silentscan/catalog/check-constraint-null-not-handled", 68, 16, 4, 0),
        new("silentscan/catalog/max-typed-column", 242, 0, 20, 0),
        new("silentscan/catalog/non-persisted-computed-column", 41, 20, 0, 0),
        new("silentscan/catalog/tvf-return-database-collation", 27, 20, 0, 0),
        new("silentscan/catalog/untrusted-check-constraint", 16, 16, 0, 0),
        new("silentscan/catalog/with-recompile", 2, 2, 0, 0),
        new("silentscan/control-flow/case-expression-missing-else", 55, 9, 11, 0),
        new("silentscan/control-flow/unassigned-output-parameter", 627, 17, 3, 0),
        new("silentscan/control-flow/unresolved-transaction", 353, 7, 13, 0),
        new("silentscan/correctness/nonunique-update-source", 3, 2, 1, 0),
        new("silentscan/correctness/not-in-nullable-subquery", 5, 3, 1, 1),
        new("silentscan/correctness/not-null-predicate-contradiction", 2, 2, 0, 0),
        new("silentscan/deprecated-syntax/equals-null-comparison", 9, 4, 5, 0),
        new("silentscan/deprecated-syntax/not-equals-null-comparison", 1, 1, 0, 0),
        new("silentscan/dml/self-referencing", 362, 13, 7, 0),
        new("silentscan/dynamic-sql/concatenated-value-in-constant-sql", 1, 0, 1, 0),
        new("silentscan/dynamic-sql/exec-string-concatenates-parameterizable-value", 1, 0, 1, 0),
        new("silentscan/forced-serial/fast-forward-cursor", 119, 20, 0, 0),
        new("silentscan/forced-serial/nonparallelizable-intrinsic", 5, 5, 0, 0),
        new("silentscan/forced-serial/table-variable-modification", 1361, 20, 0, 0),
        new("silentscan/hint/index-not-seekable", 1, 0, 1, 0),
        new("silentscan/index-design/duplicate-index", 1, 0, 1, 0),
        new("silentscan/index-design/filter-column-not-in-index", 9, 0, 9, 0),
        new("silentscan/index-design/float-or-real-index-key-column", 1, 1, 0, 0),
        new("silentscan/index-design/heap-with-nonclustered-indexes", 88, 20, 0, 0),
        new("silentscan/index-design/subsumed-index", 2, 2, 0, 0),
        new("silentscan/index-design/unindexed-foreign-key", 856, 20, 0, 0),
        new("silentscan/index-shape/composite-leading-column-unconstrained", 898, 12, 8, 0),
        new("silentscan/index/key-lookup-prone", 599, 15, 5, 0),
        new("silentscan/join/cartesian-comma-join", 1, 0, 1, 0),
        new("silentscan/join/outer-join-predicate-collapse", 87, 15, 5, 0),
        new("silentscan/lineage/expression-derived-column", 3199, 2, 18, 0),
        new("silentscan/lineage/multi-referenced-cte", 151, 20, 0, 0),
        new("silentscan/predicate/plus-operator-null-propagation", 18, 17, 1, 0),
        new("silentscan/predicates/catch-all-parameter", 5, 5, 0, 0),
        new("silentscan/predicates/float-equality", 29, 2, 18, 0),
        new("silentscan/predicates/float-order-dependent-aggregate", 3, 3, 0, 0),
        new("silentscan/predicates/isnull-replacement-value-truncation", 11, 4, 7, 0),
        new("silentscan/predicates/under-length-parameter", 39, 7, 13, 0),
        new("silentscan/query/count-star-variable-existence-check", 3, 3, 0, 0),
        new("silentscan/query/table-variable-low-compat-estimate", 1615, 20, 0, 0),
        new("silentscan/scalar-udf/in-computed-column-or-constraint", 45, 6, 14, 0),
        new("silentscan/scalar-udf/in-predicate", 612, 20, 0, 0),
        new("silentscan/scalar-udf/in-select-or-expression", 1820, 19, 1, 0),
        new("silentscan/scalar-udf/nested-under-view-or-tvf", 1234, 2, 18, 0),
        new("silentscan/tier1/case-fold-on-column", 6, 4, 2, 0),
        new("silentscan/tier1/cast-or-convert-on-column", 5, 4, 1, 0),
        new("silentscan/tier1/charindex-or-left-on-column", 2, 2, 0, 0),
        new("silentscan/tier1/column-arithmetic", 52, 14, 6, 0),
        new("silentscan/tier1/date-function-on-column", 70, 4, 16, 0),
        new("silentscan/tier1/function-wrapped-column", 64, 8, 12, 0),
        new("silentscan/tier1/leading-wildcard-like", 2, 2, 0, 0),
        new("silentscan/trigger/multi-row-unsafe-single-row-assignment", 1, 1, 0, 0),
        new("silentscan/trigger/update-function-without-value-comparison", 13, 13, 0, 0),
        new("silentscan/tvf-fence/correlated-apply", 92, 20, 0, 0),
        new("silentscan/tvf-fence/nested-under-view-or-tvf", 11, 7, 4, 0),
        new("silentscan/verdict/range-seek", 5, 5, 0, 0),
        new("silentscan/verdict/scan-forced", 252, 1, 19, 0),
        new("silentscan/view/top-percent-order-by-no-op", 3, 3, 0, 0),
        new("silentscan/window-frame/implicit-default-range", 2, 2, 0, 0),
        new("silentscan/write-loss/approximate-to-exact-truncation", 34, 11, 9, 0),
        new("silentscan/write-loss/length-truncation", 1226, 7, 13, 0),
        new("silentscan/write-loss/numeric-scale-narrowing", 244, 20, 0, 0),
        new("silentscan/write-loss/temporal-precision-loss", 111, 10, 10, 0),
        new("silentscan/write-loss/unicode-to-non-unicode", 421, 1, 19, 0),
    ];
}

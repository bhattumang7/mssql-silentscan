using SilentScan.Core.Predicates;
namespace SilentScan.Core.Rules;

public static class FindingRuleIds
{
    public const string ExpressionDerivedRuleId = "silentscan/lineage/expression-derived-column";
    public const string WriteLossUnicodeReplacementRuleId = "silentscan/write-loss/unicode-to-non-unicode";
    public const string WriteLossApproximateTruncationRuleId = "silentscan/write-loss/approximate-to-exact-truncation";
    public const string WriteLossNumericScaleNarrowingRuleId = "silentscan/write-loss/numeric-scale-narrowing";
    public const string WriteLossTemporalPrecisionLossRuleId = "silentscan/write-loss/temporal-precision-loss";
    public const string WriteLossLengthTruncationRuleId = "silentscan/write-loss/length-truncation";
    public const string WriteLossTemporalScaleNarrowingRuleId = "silentscan/write-loss/temporal-scale-narrowing";
    public const string WriteLossTemporalOffsetDroppedRuleId = "silentscan/write-loss/temporal-offset-dropped";
    public const string WriteLossSmallDateTimeRoundingRuleId = "silentscan/write-loss/smalldatetime-rounding";
    public const string TvfFenceCorrelatedApplyRuleId = "silentscan/tvf-fence/correlated-apply";
    public const string TvfFenceNestedUnderViewOrTvfRuleId = "silentscan/tvf-fence/nested-under-view-or-tvf";
    public const string ScalarUdfPredicateInvocationRuleId = "silentscan/scalar-udf/in-predicate";
    public const string ScalarUdfNestedUnderViewOrTvfRuleId = "silentscan/scalar-udf/nested-under-view-or-tvf";
    public const string ScalarUdfSchemaDependencyRuleId = "silentscan/scalar-udf/in-computed-column-or-constraint";
    public const string ScalarUdfProjectionInvocationRuleId = "silentscan/scalar-udf/in-select-or-expression";
    public const string ColumnAnsiPaddingOffRuleId = "silentscan/catalog/column-ansi-padding-off";
    public const string ProcCallArgumentMismatchRuleId = "silentscan/call-graph/argument-type-mismatch";
    public const string TvfCallArgumentMismatchRuleId = "silentscan/call-graph/tvf-argument-type-mismatch";
    public const string ProcCallTableValuedArgumentMismatchRuleId = "silentscan/call-graph/table-valued-argument-column-mismatch";
    public const string SpExecuteSqlParameterMismatchRuleId = "silentscan/call-graph/sp-executesql-parameter-type-mismatch";
    public const string TemporalBoundaryPrecisionRuleId = "silentscan/correctness/between-end-of-period-boundary";
    public static string MaxTypedColumnRuleId(NonIndexableColumnFindingKind kind) => kind switch
    {
        NonIndexableColumnFindingKind.MaxLength => "silentscan/catalog/max-typed-column",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled NonIndexableColumnFindingKind."),
    };
    public static string DynamicDataMaskingRuleId(DynamicDataMaskingFindingKind kind) => kind switch
    {
        DynamicDataMaskingFindingKind.PredicateExposure => "silentscan/predicates/dynamic-data-masking-predicate-exposure",
        DynamicDataMaskingFindingKind.ComputedExpressionCollapse => "silentscan/predicates/dynamic-data-masking-computed-expression-collapse",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled DynamicDataMaskingFindingKind."),
    };
    public const string FloatEqualityRuleId = "silentscan/predicates/float-equality";
    public const string FloatOrderDependentAggregateRuleId = "silentscan/predicates/float-order-dependent-aggregate";
    public const string IsNullReplacementValueTruncationRuleId = "silentscan/predicates/isnull-replacement-value-truncation";
    public const string TriggerOrderRuleId = "silentscan/catalog/trigger-firing-order-undefined";
    public const string MemoryOptimizedSchemaOnlyDurabilityRuleId = "silentscan/catalog/memory-optimized-schema-only-durability";
    public const string QueryAntiPatternTableVariableLowCompatEstimateRuleId = "silentscan/query/table-variable-low-compat-estimate";
    public const string QueryAntiPatternTableVariablePspSkipRuleId = "silentscan/query/table-variable-psp-skip";
    public const string QueryAntiPatternTableVariableStaleEstimateInLoopRuleId = "silentscan/query/table-variable-stale-estimate-in-loop";
    public const string QueryAntiPatternRbarSingleRowLoopDmlRuleId = "silentscan/query/rbar-single-row-loop-dml";
    public const string QueryAntiPatternCountStarVariableExistenceCheckRuleId = "silentscan/query/count-star-variable-existence-check";
    public const string QueryAntiPatternNonAggregateHavingPredicateRuleId = "silentscan/query/non-aggregate-having-predicate";
    public const string QueryAntiPatternDistinctMaskingJoinFanoutRuleId = "silentscan/query/distinct-masking-join-fanout";
    public const string QueryAntiPatternUnqualifiedTableReferenceRuleId = "silentscan/query/unqualified-table-reference";
    public const string QueryAntiPatternMergeMissingHoldlockRuleId = "silentscan/query/merge-missing-holdlock";
    public const string QueryAntiPatternMergeUnconditionalDeleteRuleId = "silentscan/query/merge-unconditional-delete";
    public const string QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId = "silentscan/query/multi-row-insert-ignore-dup-key-drop";
    public const string IndexCoverageKeyLookupProneIndexRuleId = "silentscan/index/key-lookup-prone";
    public const string TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId = "silentscan/trigger/multi-row-unsafe-single-row-assignment";
    public const string TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId = "silentscan/trigger/multi-row-unsafe-keyed-dml";
    public const string TriggerCorrectnessDirectRecursiveTriggerRuleId = "silentscan/trigger/direct-recursive-trigger";
    public const string TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId = "silentscan/trigger/instead-of-insert-filtered-no-reject-path";
    public const string TriggerCorrectnessUpdateFunctionWithoutValueComparisonRuleId = "silentscan/trigger/update-function-without-value-comparison";
    public const string CrossModuleLockOrderRuleId = "silentscan/cross-module/inconsistent-lock-order";
    public const string UnderLengthParameterRuleId = "silentscan/predicates/under-length-parameter";
    public const string AnsiPaddingMismatchRuleId = "silentscan/predicates/ansi-padding-mismatch";
    public const string CatchAllPredicateRuleId = "silentscan/predicates/catch-all-parameter";
    public const string LocalVariablePredicateRuleId = "silentscan/predicates/local-variable-predicate";
    public const string FilteredIndexParameterMismatchRuleId = "silentscan/predicates/filtered-index-parameter-mismatch";
    public const string ParameterReassignmentPredicateRuleId = "silentscan/predicates/reassigned-parameter";
    public const string DeprecatedSyntaxEqualsNullComparisonRuleId = "silentscan/deprecated-syntax/equals-null-comparison";
    public const string DeprecatedSyntaxNotEqualsNullComparisonRuleId = "silentscan/deprecated-syntax/not-equals-null-comparison";
    public const string ControlFlowRiskDirtyReadIsolationHintRuleId = "silentscan/control-flow/dirty-read-isolation-hint";
    public const string ControlFlowRiskReadCommittedLockRevertsRowVersioningRuleId = "silentscan/control-flow/read-committed-lock-reverts-row-versioning";
    public const string ControlFlowRiskLegacyIdentityIntrinsicRuleId = "silentscan/control-flow/legacy-identity-intrinsic";
    public const string ControlFlowRiskCaseExpressionMissingElseRuleId = "silentscan/control-flow/case-expression-missing-else";
    public const string ControlFlowRiskNonDeterministicCaseInputRuleId = "silentscan/control-flow/non-deterministic-case-input";
    public const string NotInNullableSubqueryRuleId = "silentscan/correctness/not-in-nullable-subquery";
    public const string NonUniqueUpdateSourceRuleId = "silentscan/correctness/nonunique-update-source";
    public const string CheckConstraintPredicateContradictionIntervalRuleId = "silentscan/correctness/check-constraint-predicate-contradiction";
    public const string NotNullPredicateContradictionRuleId = "silentscan/correctness/not-null-predicate-contradiction";
    public const string ForcedSerialTableVariableModificationRuleId = "silentscan/forced-serial/table-variable-modification";
    public const string ForcedSerialFastForwardCursorRuleId = "silentscan/forced-serial/fast-forward-cursor";
    public const string ForcedSerialNonParallelizableIntrinsicRuleId = "silentscan/forced-serial/nonparallelizable-intrinsic";
    public const string UntrustedCheckConstraintRuleId = "silentscan/catalog/untrusted-check-constraint";
    public const string MultiReferencedCteRuleId = "silentscan/lineage/multi-referenced-cte";
    public const string PostExpansionJoinWidthRuleId = "silentscan/lineage/post-expansion-join-width";
    public const string SelectStarViewRuleId = "silentscan/lineage/select-star-view";
    public const string PartialCompositeForeignKeyJoinRuleId = "silentscan/join/partial-composite-fk";
    public const string OuterJoinPredicateCollapseRuleId = "silentscan/join/outer-join-predicate-collapse";
    public const string ConcatenatedValueInConstantSqlRuleId = "silentscan/dynamic-sql/concatenated-value-in-constant-sql";
    public const string ExecStringConcatenatesParameterizableValueRuleId = "silentscan/dynamic-sql/exec-string-concatenates-parameterizable-value";
    public const string TempTableExecShapeColumnTypeMismatchRuleId = "silentscan/dynamic-sql/insert-exec-temp-table-column-type-mismatch";
    public const string ExecResultSetsShapeColumnTypeMismatchRuleId = "silentscan/dynamic-sql/exec-with-result-sets-column-type-mismatch";
    public const string NonPersistedComputedColumnRuleId = "silentscan/catalog/non-persisted-computed-column";
    public const string SelfReferencingDmlRuleId = "silentscan/dml/self-referencing";
    public const string TemporalTableHistoryIndexGapRuleId = "silentscan/catalog/temporal-history-index-gap";
    public const string GeneratedAlwaysColumnExplicitInsertRuleId = "silentscan/catalog/generated-always-column-explicit-insert";
    public const string GeneratedAlwaysColumnExplicitUpdateRuleId = "silentscan/catalog/generated-always-column-explicit-update";
    public const string CheckConstraintNullNotHandledRuleId = "silentscan/catalog/check-constraint-null-not-handled";
    public const string CheckConstraintOnIdentityColumnRuleId = "silentscan/catalog/check-constraint-on-identity-column";
    public const string StaleSelectStarViewRuleId = "silentscan/catalog/stale-select-star-view";
    public const string BareTopNoOrderByRuleId = "silentscan/query/bare-top-no-order-by";
    public const string StringAggMissingOrderRuleId = "silentscan/string-builtin/string-agg-missing-order";
    public const string ForXmlPathMissingOrderRuleId = "silentscan/query/for-xml-path-missing-order";
    public const string JsonArrayAggMissingOrderRuleId = "silentscan/json-builtin/json-arrayagg-missing-order";
    public const string JsonObjectDuplicateKeyRuleId = "silentscan/json-builtin/json-object-duplicate-key";
    public const string UnistrUnpairedSurrogateRuleId = "silentscan/string-builtin/unistr-unpaired-surrogate";
    public const string RegexpReplaceDollarBackreferenceRuleId = "silentscan/string-builtin/regexp-replace-dollar-backreference";
    public const string RegexpDefaultCaseSensitiveOnCiColumnRuleId = "silentscan/string-builtin/regexp-default-case-sensitive-on-ci-collation";
    public const string RegexpAccentInsensitiveColumnRuleId = "silentscan/string-builtin/regexp-accent-insensitive-column-unsupported";
    public const string StringConcatNullRuleId = "silentscan/predicate/plus-operator-null-propagation";
    public const string SecurityPredicateIndexRuleId = "silentscan/catalog/rls-predicate-unindexed-column";
    public static string ModuleCompileFlagRuleId(ModuleCompileFlagFindingKind kind) => kind switch
    {
        ModuleCompileFlagFindingKind.RecompilesEveryCall => "silentscan/catalog/with-recompile",
        ModuleCompileFlagFindingKind.TableValuedFunctionReturnUsesDatabaseCollation => "silentscan/catalog/tvf-return-database-collation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string WindowFrameRuleId(WindowFrameFindingKind kind) => kind switch
    {
        WindowFrameFindingKind.ImplicitDefaultRangeFrame => "silentscan/window-frame/implicit-default-range",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string BoundedStringBuiltinTruncationRuleId(BoundedStringBuiltinTruncationFindingKind kind) => kind switch
    {
        BoundedStringBuiltinTruncationFindingKind.ReplicateResultTruncated => "silentscan/string-builtin/replicate-truncated",
        BoundedStringBuiltinTruncationFindingKind.ReplaceResultTruncated => "silentscan/string-builtin/replace-truncated",
        BoundedStringBuiltinTruncationFindingKind.SpaceResultTruncated => "silentscan/string-builtin/space-truncated",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string CursorCloseOnCommitRuleId = "silentscan/control-flow/cursor-close-on-commit";
    public static string TransactionHygieneRuleId(TransactionHygieneFindingKind kind) => kind switch
    {
        TransactionHygieneFindingKind.UnresolvedOnSomePath => "silentscan/control-flow/unresolved-transaction",
        TransactionHygieneFindingKind.ImplicitTransactionUnresolvedOnSomePath => "silentscan/control-flow/unresolved-implicit-transaction",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string OutputParameterRuleId = "silentscan/control-flow/unassigned-output-parameter";
    public const string CompositeIndexLeadingColumnRuleId = "silentscan/index-shape/composite-leading-column-unconstrained";
    public const string MissingStatisticsRuleId = "silentscan/statistics/no-applicable-statistic-auto-create-disabled";
    public static string IndexHintRuleId(IndexHintFindingKind kind) => kind switch
    {
        IndexHintFindingKind.HintedIndexNotSeekable => "silentscan/hint/index-not-seekable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string AmbiguousDateLiteralConversionRuleId = "silentscan/session-date/ambiguous-date-literal-conversion";
    public static string CartesianJoinRuleId(CartesianJoinKind kind) => kind switch
    {
        CartesianJoinKind.CommaJoin => "silentscan/join/cartesian-comma-join",
        CartesianJoinKind.ExplicitCrossJoin => "silentscan/join/cartesian-cross-join",
        CartesianJoinKind.AlwaysFalseInnerJoinPredicate => "silentscan/join/always-false-inner-join-predicate",
        CartesianJoinKind.JoinPredicateEmptyWithWhereClause => "silentscan/join/join-predicate-empty-with-where-clause",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public const string TruncateSwallowedRuleId = "silentscan/control-flow/truncate-swallowed-by-catch";
    public static string DatabaseConfigurationRuleId(DatabaseConfigurationFindingKind kind) => kind switch
    {
        DatabaseConfigurationFindingKind.AutoShrinkOn => "silentscan/database/auto-shrink-on",
        DatabaseConfigurationFindingKind.AutoCloseOn => "silentscan/database/auto-close-on",
        DatabaseConfigurationFindingKind.AutoCreateStatisticsOff => "silentscan/database/auto-create-statistics-off",
        DatabaseConfigurationFindingKind.AutoUpdateStatisticsOff => "silentscan/database/auto-update-statistics-off",
        DatabaseConfigurationFindingKind.SpatialPersistedComputedColumnDisabledOnCompatibilityLevelChange => "silentscan/database/spatial-persisted-computed-column-compatibility-change",
        DatabaseConfigurationFindingKind.PlanGuideAltersOptimization => "silentscan/database/plan-guide-alters-optimization",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string UnindexedTempTableUsageRuleId(UnindexedTempTableUsageKind kind) => kind switch
    {
        UnindexedTempTableUsageKind.JoinOperand => "silentscan/temp-table/unindexed-join-operand",
        UnindexedTempTableUsageKind.FilteredInWhere => "silentscan/temp-table/unindexed-where-filter",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string ViewOrderingRuleId(ViewOrderingFindingKind kind) => kind switch
    {
        ViewOrderingFindingKind.TopPercentOrderByNeverLimits => "silentscan/view/top-percent-order-by-no-op",
        ViewOrderingFindingKind.OrderByNotGuaranteedToConsumer => "silentscan/view/order-by-not-guaranteed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string IndexDesignRuleId(IndexDesignFindingKind kind) => kind switch
    {
        IndexDesignFindingKind.HeapWithNonclusteredIndexes => "silentscan/index-design/heap-with-nonclustered-indexes",
        IndexDesignFindingKind.RandomClusteredKeyGuidDefault => "silentscan/index-design/random-clustered-key-guid-default",
        IndexDesignFindingKind.DuplicateIndex => "silentscan/index-design/duplicate-index",
        IndexDesignFindingKind.SubsumedIndex => "silentscan/index-design/subsumed-index",
        IndexDesignFindingKind.UnindexedForeignKey => "silentscan/index-design/unindexed-foreign-key",
        IndexDesignFindingKind.FilterColumnNotInIndex => "silentscan/index-design/filter-column-not-in-index",
        IndexDesignFindingKind.FloatOrRealIndexKeyColumn => "silentscan/index-design/float-or-real-index-key-column",
        IndexDesignFindingKind.NoRecomputeStatistics => "silentscan/index-design/no-recompute-statistics",
        IndexDesignFindingKind.MonotonicClusteredKeyMissingSequentialOptimization => "silentscan/index-design/monotonic-clustered-key-missing-sequential-optimization",
        IndexDesignFindingKind.RowOrPageLockingDisabled => "silentscan/index-design/row-or-page-locking-disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string ForcedParameterizationRuleId(ForcedParameterizationFindingKind kind) => kind switch
    {
        ForcedParameterizationFindingKind.LikePatternLiteral => "silentscan/forced-parameterization/like-pattern-literal",
        ForcedParameterizationFindingKind.TopOrPagingLiteral => "silentscan/forced-parameterization/top-or-paging-literal",
        ForcedParameterizationFindingKind.SelectListLiteral => "silentscan/forced-parameterization/select-list-literal",
        ForcedParameterizationFindingKind.HavingLiteral => "silentscan/forced-parameterization/having-literal",
        ForcedParameterizationFindingKind.OrderByExpressionLiteral => "silentscan/forced-parameterization/order-by-expression-literal",
        ForcedParameterizationFindingKind.TableSampleSizeLiteral => "silentscan/forced-parameterization/table-sample-size-literal",
        ForcedParameterizationFindingKind.DmlOutputListLiteral => "silentscan/forced-parameterization/dml-output-list-literal",
        ForcedParameterizationFindingKind.ConvertStyleCodeLiteral => "silentscan/forced-parameterization/convert-style-code-literal",
        ForcedParameterizationFindingKind.ConstantFoldableExpressionLiteral => "silentscan/forced-parameterization/constant-foldable-expression-literal",
        ForcedParameterizationFindingKind.GroupByExpressionLiteral => "silentscan/forced-parameterization/group-by-expression-literal",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public static string SetOptionRuleId(SetOptionFindingKind kind) => kind switch
    {
        SetOptionFindingKind.QuotedIdentifierOffBlocksIndexedFeature => "silentscan/set-option/quoted-identifier-off",
        SetOptionFindingKind.NumericRoundabortOnBlocksIndexedFeature => "silentscan/set-option/numeric-roundabort-on",
        SetOptionFindingKind.AnsiNullsOffBlocksIndexedFeature => "silentscan/set-option/ansi-nulls-off",
        SetOptionFindingKind.AnsiWarningsOffBlocksIndexedFeature => "silentscan/set-option/ansi-warnings-off",
        SetOptionFindingKind.ConcatNullYieldsNullOffBlocksIndexedFeature => "silentscan/set-option/concat-null-yields-null-off",
        SetOptionFindingKind.AnsiPaddingOffBlocksIndexedFeature => "silentscan/set-option/ansi-padding-off",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled SetOptionFindingKind."),
    };
    public static string UnparameterizedDynamicSqlRuleId(UnparameterizedDynamicSqlFindingKind kind) => kind switch
    {
        UnparameterizedDynamicSqlFindingKind.ConcatenatedValueInConstantSql => ConcatenatedValueInConstantSqlRuleId,
        UnparameterizedDynamicSqlFindingKind.ExecStringConcatenatesParameterizableValue => ExecStringConcatenatesParameterizableValueRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled UnparameterizedDynamicSqlFindingKind."),
    };
    public static string TempTableExecShapeRuleId(TempTableExecShapeFindingKind kind) => kind switch
    {
        TempTableExecShapeFindingKind.ColumnTypeMismatch => TempTableExecShapeColumnTypeMismatchRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled TempTableExecShapeFindingKind."),
    };
    public static string ExecResultSetsShapeRuleId(ExecResultSetsShapeFindingKind kind) => kind switch
    {
        ExecResultSetsShapeFindingKind.ColumnTypeMismatch => ExecResultSetsShapeColumnTypeMismatchRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled ExecResultSetsShapeFindingKind."),
    };
    public static string ForcedSerialRuleId(ForcedSerialFindingKind kind) => kind switch
    {
        ForcedSerialFindingKind.TableVariableModification => ForcedSerialTableVariableModificationRuleId,
        ForcedSerialFindingKind.FastForwardCursor => ForcedSerialFastForwardCursorRuleId,
        ForcedSerialFindingKind.NonParallelizableIntrinsic => ForcedSerialNonParallelizableIntrinsicRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled ForcedSerialFindingKind."),
    };
    public static string DeprecatedSyntaxRuleId(DeprecatedSyntaxFindingKind kind) => kind switch
    {
        DeprecatedSyntaxFindingKind.EqualsNullComparison => DeprecatedSyntaxEqualsNullComparisonRuleId,
        DeprecatedSyntaxFindingKind.NotEqualsNullComparison => DeprecatedSyntaxNotEqualsNullComparisonRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled DeprecatedSyntaxFindingKind."),
    };
    public static string ControlFlowRiskRuleId(ControlFlowRiskFindingKind kind) => kind switch
    {
        ControlFlowRiskFindingKind.DirtyReadIsolationHint => ControlFlowRiskDirtyReadIsolationHintRuleId,
        ControlFlowRiskFindingKind.ReadCommittedLockRevertsRowVersioning => ControlFlowRiskReadCommittedLockRevertsRowVersioningRuleId,
        ControlFlowRiskFindingKind.LegacyIdentityIntrinsic => ControlFlowRiskLegacyIdentityIntrinsicRuleId,
        ControlFlowRiskFindingKind.CaseExpressionMissingElse => ControlFlowRiskCaseExpressionMissingElseRuleId,
        ControlFlowRiskFindingKind.NonDeterministicCaseInput => ControlFlowRiskNonDeterministicCaseInputRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled ControlFlowRiskFindingKind."),
    };
    public static string QueryAntiPatternRuleId(QueryAntiPatternFindingKind kind) => kind switch
    {
        QueryAntiPatternFindingKind.TableVariablePspSkip => QueryAntiPatternTableVariablePspSkipRuleId,
        QueryAntiPatternFindingKind.TableVariableLowCompatEstimate => QueryAntiPatternTableVariableLowCompatEstimateRuleId,
        QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop => QueryAntiPatternTableVariableStaleEstimateInLoopRuleId,
        QueryAntiPatternFindingKind.RbarSingleRowLoopDml => QueryAntiPatternRbarSingleRowLoopDmlRuleId,
        QueryAntiPatternFindingKind.CountStarVariableExistenceCheck => QueryAntiPatternCountStarVariableExistenceCheckRuleId,
        QueryAntiPatternFindingKind.NonAggregateHavingPredicate => QueryAntiPatternNonAggregateHavingPredicateRuleId,
        QueryAntiPatternFindingKind.DistinctMaskingJoinFanout => QueryAntiPatternDistinctMaskingJoinFanoutRuleId,
        QueryAntiPatternFindingKind.UnqualifiedTableReference => QueryAntiPatternUnqualifiedTableReferenceRuleId,
        QueryAntiPatternFindingKind.MergeMissingHoldlock => QueryAntiPatternMergeMissingHoldlockRuleId,
        QueryAntiPatternFindingKind.MergeUnconditionalDelete => QueryAntiPatternMergeUnconditionalDeleteRuleId,
        QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop => QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled QueryAntiPatternFindingKind."),
    };
    public static string IndexCoverageRuleId(IndexCoverageFindingKind kind) => kind switch
    {
        IndexCoverageFindingKind.KeyLookupProneIndex => IndexCoverageKeyLookupProneIndexRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled IndexCoverageFindingKind."),
    };
    public static string TriggerCorrectnessRuleId(TriggerCorrectnessFindingKind kind) => kind switch
    {
        TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment => TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId,
        TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml => TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId,
        TriggerCorrectnessFindingKind.DirectRecursiveTrigger => TriggerCorrectnessDirectRecursiveTriggerRuleId,
        TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath => TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId,
        TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison => TriggerCorrectnessUpdateFunctionWithoutValueComparisonRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled TriggerCorrectnessFindingKind."),
    };
    public static string UntrustedConstraintRuleId(UntrustedConstraintFindingKind kind) => kind switch
    {
        UntrustedConstraintFindingKind.CheckConstraint => UntrustedCheckConstraintRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled UntrustedConstraintFindingKind."),
    };
    public static string CheckConstraintRuleId(CheckConstraintFindingKind kind) => kind switch
    {
        CheckConstraintFindingKind.NullNotHandled => CheckConstraintNullNotHandledRuleId,
        CheckConstraintFindingKind.ConstraintOnIdentityColumn => CheckConstraintOnIdentityColumnRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled CheckConstraintFindingKind."),
    };
    public static string CheckConstraintPredicateContradictionRuleId(CheckConstraintPredicateContradictionKind kind) => kind switch
    {
        CheckConstraintPredicateContradictionKind.CheckConstraintInterval => CheckConstraintPredicateContradictionIntervalRuleId,
        CheckConstraintPredicateContradictionKind.NotNullConstraint => NotNullPredicateContradictionRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled CheckConstraintPredicateContradictionKind."),
    };
    public static string TvfFenceRuleId(TvfFenceFindingKind kind) => kind switch
    {
        TvfFenceFindingKind.CorrelatedApply => TvfFenceCorrelatedApplyRuleId,
        TvfFenceFindingKind.NestedUnderViewOrTvf => TvfFenceNestedUnderViewOrTvfRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled TvfFenceFindingKind."),
    };
    public static string ScalarUdfRuleId(ScalarUdfFindingKind kind) => kind switch
    {
        ScalarUdfFindingKind.PredicateInvocation => ScalarUdfPredicateInvocationRuleId,
        ScalarUdfFindingKind.NestedUnderViewOrTvf => ScalarUdfNestedUnderViewOrTvfRuleId,
        ScalarUdfFindingKind.SchemaDependency => ScalarUdfSchemaDependencyRuleId,
        ScalarUdfFindingKind.ProjectionInvocation => ScalarUdfProjectionInvocationRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled ScalarUdfFindingKind."),
    };
    public static string WriteLossRuleId(WriteLossKind kind) => kind switch
    {
        WriteLossKind.UnicodeToNonUnicodeReplacement => WriteLossUnicodeReplacementRuleId,
        WriteLossKind.ApproximateToExactTruncation => WriteLossApproximateTruncationRuleId,
        WriteLossKind.NumericScaleNarrowing => WriteLossNumericScaleNarrowingRuleId,
        WriteLossKind.TemporalPrecisionLoss => WriteLossTemporalPrecisionLossRuleId,
        WriteLossKind.LengthTruncation => WriteLossLengthTruncationRuleId,
        WriteLossKind.TemporalScaleNarrowing => WriteLossTemporalScaleNarrowingRuleId,
        WriteLossKind.TemporalOffsetDropped => WriteLossTemporalOffsetDroppedRuleId,
        WriteLossKind.SmallDateTimeRounding => WriteLossSmallDateTimeRoundingRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled WriteLossKind."),
    };
    public static string Tier1RuleId(SargabilityFindingKind kind) => kind switch
    {
        SargabilityFindingKind.FunctionWrappedColumn => "silentscan/tier1/function-wrapped-column",
        SargabilityFindingKind.CastOrConvertOnColumn => "silentscan/tier1/cast-or-convert-on-column",
        SargabilityFindingKind.ColumnArithmetic => "silentscan/tier1/column-arithmetic",
        SargabilityFindingKind.LeadingWildcardLike => "silentscan/tier1/leading-wildcard-like",
        SargabilityFindingKind.RegexpPatternNoLiteralPrefix => "silentscan/tier1/regexp-pattern-no-literal-prefix",
        SargabilityFindingKind.CaseFoldOnColumn => "silentscan/tier1/case-fold-on-column",
        SargabilityFindingKind.DateFunctionOnColumn => "silentscan/tier1/date-function-on-column",
        SargabilityFindingKind.CharindexOrLeftOnColumn => "silentscan/tier1/charindex-or-left-on-column",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled SargabilityFindingKind."),
    };
    public static string VerdictRuleId(Verdict verdict) => verdict switch
    {
        Verdict.ScanForced => "silentscan/verdict/scan-forced",
        Verdict.RangeSeek => "silentscan/verdict/range-seek",
        Verdict.SeekPreserved => "silentscan/verdict/seek-preserved",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unhandled Verdict."),
    };
}

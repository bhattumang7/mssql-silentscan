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
    public const string MaxTypedColumnMaxLengthRuleId = "silentscan/catalog/max-typed-column";
    public static string MaxTypedColumnRuleId(NonIndexableColumnFindingKind kind) => kind switch
    {
        NonIndexableColumnFindingKind.MaxLength => MaxTypedColumnMaxLengthRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled NonIndexableColumnFindingKind."),
    };
    public const string DynamicDataMaskingPredicateExposureRuleId = "silentscan/predicates/dynamic-data-masking-predicate-exposure";
    public const string DynamicDataMaskingComputedExpressionCollapseRuleId = "silentscan/predicates/dynamic-data-masking-computed-expression-collapse";
    public static string DynamicDataMaskingRuleId(DynamicDataMaskingFindingKind kind) => kind switch
    {
        DynamicDataMaskingFindingKind.PredicateExposure => DynamicDataMaskingPredicateExposureRuleId,
        DynamicDataMaskingFindingKind.ComputedExpressionCollapse => DynamicDataMaskingComputedExpressionCollapseRuleId,
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
    public const string PartialCompositeForeignKeyJoinRuleId = "silentscan/join/partial-composite-fk";
    public const string OuterJoinPredicateCollapseRuleId = "silentscan/join/outer-join-predicate-collapse";
    public const string ConcatenatedValueInConstantSqlRuleId = "silentscan/dynamic-sql/concatenated-value-in-constant-sql";
    public const string ExecStringConcatenatesParameterizableValueRuleId = "silentscan/dynamic-sql/exec-string-concatenates-parameterizable-value";
    public const string TempTableExecShapeColumnTypeMismatchRuleId = "silentscan/dynamic-sql/insert-exec-temp-table-column-type-mismatch";
    public const string ExecResultSetsShapeColumnTypeMismatchRuleId = "silentscan/dynamic-sql/exec-with-result-sets-column-type-mismatch";
    public const string NonPersistedComputedColumnRuleId = "silentscan/catalog/non-persisted-computed-column";
    public const string SelfReferencingDmlRuleId = "silentscan/dml/self-referencing";
    public const string TemporalTableHistoryIndexGapRuleId = "silentscan/catalog/temporal-history-index-gap";
    public const string CheckConstraintNullNotHandledRuleId = "silentscan/catalog/check-constraint-null-not-handled";
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
    public const string ModuleCompileFlagRecompilesEveryCallRuleId = "silentscan/catalog/with-recompile";
    public const string ModuleCompileFlagTableValuedFunctionReturnUsesDatabaseCollationRuleId = "silentscan/catalog/tvf-return-database-collation";
    public static string ModuleCompileFlagRuleId(ModuleCompileFlagFindingKind kind) => kind switch
    {
        ModuleCompileFlagFindingKind.RecompilesEveryCall => ModuleCompileFlagRecompilesEveryCallRuleId,
        ModuleCompileFlagFindingKind.TableValuedFunctionReturnUsesDatabaseCollation => ModuleCompileFlagTableValuedFunctionReturnUsesDatabaseCollationRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string WindowFrameImplicitDefaultRangeFrameRuleId = "silentscan/window-frame/implicit-default-range";
    public static string WindowFrameRuleId(WindowFrameFindingKind kind) => kind switch
    {
        WindowFrameFindingKind.ImplicitDefaultRangeFrame => WindowFrameImplicitDefaultRangeFrameRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId = "silentscan/string-builtin/replicate-truncated";
    public const string BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId = "silentscan/string-builtin/replace-truncated";
    public const string BoundedStringBuiltinTruncationSpaceResultTruncatedRuleId = "silentscan/string-builtin/space-truncated";
    public static string BoundedStringBuiltinTruncationRuleId(BoundedStringBuiltinTruncationFindingKind kind) => kind switch
    {
        BoundedStringBuiltinTruncationFindingKind.ReplicateResultTruncated => BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId,
        BoundedStringBuiltinTruncationFindingKind.ReplaceResultTruncated => BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId,
        BoundedStringBuiltinTruncationFindingKind.SpaceResultTruncated => BoundedStringBuiltinTruncationSpaceResultTruncatedRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string TransactionHygieneImplicitTransactionUnresolvedOnSomePathRuleId = "silentscan/control-flow/unresolved-implicit-transaction";
    public static string TransactionHygieneRuleId(TransactionHygieneFindingKind kind) => kind switch
    {
        TransactionHygieneFindingKind.ImplicitTransactionUnresolvedOnSomePath => TransactionHygieneImplicitTransactionUnresolvedOnSomePathRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string OutputParameterRuleId = "silentscan/control-flow/unassigned-output-parameter";
    public const string CompositeIndexLeadingColumnRuleId = "silentscan/index-shape/composite-leading-column-unconstrained";
    public const string MissingStatisticsRuleId = "silentscan/statistics/no-applicable-statistic-auto-create-disabled";
    public const string IndexHintHintedIndexNotSeekableRuleId = "silentscan/hint/index-not-seekable";
    public static string IndexHintRuleId(IndexHintFindingKind kind) => kind switch
    {
        IndexHintFindingKind.HintedIndexNotSeekable => IndexHintHintedIndexNotSeekableRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string AmbiguousDateLiteralConversionRuleId = "silentscan/session-date/ambiguous-date-literal-conversion";
    public const string CartesianJoinCommaJoinRuleId = "silentscan/join/cartesian-comma-join";
    public const string CartesianJoinExplicitCrossJoinRuleId = "silentscan/join/cartesian-cross-join";
    public const string CartesianJoinAlwaysFalseInnerJoinPredicateRuleId = "silentscan/join/always-false-inner-join-predicate";
    public const string CartesianJoinJoinPredicateEmptyWithWhereClauseRuleId = "silentscan/join/join-predicate-empty-with-where-clause";
    public static string CartesianJoinRuleId(CartesianJoinKind kind) => kind switch
    {
        CartesianJoinKind.CommaJoin => CartesianJoinCommaJoinRuleId,
        CartesianJoinKind.ExplicitCrossJoin => CartesianJoinExplicitCrossJoinRuleId,
        CartesianJoinKind.AlwaysFalseInnerJoinPredicate => CartesianJoinAlwaysFalseInnerJoinPredicateRuleId,
        CartesianJoinKind.JoinPredicateEmptyWithWhereClause => CartesianJoinJoinPredicateEmptyWithWhereClauseRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public const string TruncateSwallowedRuleId = "silentscan/control-flow/truncate-swallowed-by-catch";
    public const string DatabaseConfigurationAutoShrinkOnRuleId = "silentscan/database/auto-shrink-on";
    public const string DatabaseConfigurationAutoCloseOnRuleId = "silentscan/database/auto-close-on";
    public const string DatabaseConfigurationAutoCreateStatisticsOffRuleId = "silentscan/database/auto-create-statistics-off";
    public const string DatabaseConfigurationAutoUpdateStatisticsOffRuleId = "silentscan/database/auto-update-statistics-off";
    public const string DatabaseConfigurationSpatialPersistedComputedColumnDisabledOnCompatibilityLevelChangeRuleId = "silentscan/database/spatial-persisted-computed-column-compatibility-change";
    public const string DatabaseConfigurationPlanGuideAltersOptimizationRuleId = "silentscan/database/plan-guide-alters-optimization";
    public static string DatabaseConfigurationRuleId(DatabaseConfigurationFindingKind kind) => kind switch
    {
        DatabaseConfigurationFindingKind.AutoShrinkOn => DatabaseConfigurationAutoShrinkOnRuleId,
        DatabaseConfigurationFindingKind.AutoCloseOn => DatabaseConfigurationAutoCloseOnRuleId,
        DatabaseConfigurationFindingKind.AutoCreateStatisticsOff => DatabaseConfigurationAutoCreateStatisticsOffRuleId,
        DatabaseConfigurationFindingKind.AutoUpdateStatisticsOff => DatabaseConfigurationAutoUpdateStatisticsOffRuleId,
        DatabaseConfigurationFindingKind.SpatialPersistedComputedColumnDisabledOnCompatibilityLevelChange => DatabaseConfigurationSpatialPersistedComputedColumnDisabledOnCompatibilityLevelChangeRuleId,
        DatabaseConfigurationFindingKind.PlanGuideAltersOptimization => DatabaseConfigurationPlanGuideAltersOptimizationRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string UnindexedTempTableUsageJoinOperandRuleId = "silentscan/temp-table/unindexed-join-operand";
    public const string UnindexedTempTableUsageFilteredInWhereRuleId = "silentscan/temp-table/unindexed-where-filter";
    public static string UnindexedTempTableUsageRuleId(UnindexedTempTableUsageKind kind) => kind switch
    {
        UnindexedTempTableUsageKind.JoinOperand => UnindexedTempTableUsageJoinOperandRuleId,
        UnindexedTempTableUsageKind.FilteredInWhere => UnindexedTempTableUsageFilteredInWhereRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string ViewOrderingTopPercentOrderByNeverLimitsRuleId = "silentscan/view/top-percent-order-by-no-op";
    public const string ViewOrderingOrderByNotGuaranteedToConsumerRuleId = "silentscan/view/order-by-not-guaranteed";
    public static string ViewOrderingRuleId(ViewOrderingFindingKind kind) => kind switch
    {
        ViewOrderingFindingKind.TopPercentOrderByNeverLimits => ViewOrderingTopPercentOrderByNeverLimitsRuleId,
        ViewOrderingFindingKind.OrderByNotGuaranteedToConsumer => ViewOrderingOrderByNotGuaranteedToConsumerRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string IndexDesignHeapWithNonclusteredIndexesRuleId = "silentscan/index-design/heap-with-nonclustered-indexes";
    public const string IndexDesignRandomClusteredKeyGuidDefaultRuleId = "silentscan/index-design/random-clustered-key-guid-default";
    public const string IndexDesignDuplicateIndexRuleId = "silentscan/index-design/duplicate-index";
    public const string IndexDesignSubsumedIndexRuleId = "silentscan/index-design/subsumed-index";
    public const string IndexDesignUnindexedForeignKeyRuleId = "silentscan/index-design/unindexed-foreign-key";
    public const string IndexDesignFilterColumnNotInIndexRuleId = "silentscan/index-design/filter-column-not-in-index";
    public const string IndexDesignFloatOrRealIndexKeyColumnRuleId = "silentscan/index-design/float-or-real-index-key-column";
    public const string IndexDesignNoRecomputeStatisticsRuleId = "silentscan/index-design/no-recompute-statistics";
    public const string IndexDesignMonotonicClusteredKeyMissingSequentialOptimizationRuleId = "silentscan/index-design/monotonic-clustered-key-missing-sequential-optimization";
    public const string IndexDesignRowOrPageLockingDisabledRuleId = "silentscan/index-design/row-or-page-locking-disabled";
    public static string IndexDesignRuleId(IndexDesignFindingKind kind) => kind switch
    {
        IndexDesignFindingKind.HeapWithNonclusteredIndexes => IndexDesignHeapWithNonclusteredIndexesRuleId,
        IndexDesignFindingKind.RandomClusteredKeyGuidDefault => IndexDesignRandomClusteredKeyGuidDefaultRuleId,
        IndexDesignFindingKind.DuplicateIndex => IndexDesignDuplicateIndexRuleId,
        IndexDesignFindingKind.SubsumedIndex => IndexDesignSubsumedIndexRuleId,
        IndexDesignFindingKind.UnindexedForeignKey => IndexDesignUnindexedForeignKeyRuleId,
        IndexDesignFindingKind.FilterColumnNotInIndex => IndexDesignFilterColumnNotInIndexRuleId,
        IndexDesignFindingKind.FloatOrRealIndexKeyColumn => IndexDesignFloatOrRealIndexKeyColumnRuleId,
        IndexDesignFindingKind.NoRecomputeStatistics => IndexDesignNoRecomputeStatisticsRuleId,
        IndexDesignFindingKind.MonotonicClusteredKeyMissingSequentialOptimization => IndexDesignMonotonicClusteredKeyMissingSequentialOptimizationRuleId,
        IndexDesignFindingKind.RowOrPageLockingDisabled => IndexDesignRowOrPageLockingDisabledRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string ForcedParameterizationLikePatternLiteralRuleId = "silentscan/forced-parameterization/like-pattern-literal";
    public const string ForcedParameterizationTopOrPagingLiteralRuleId = "silentscan/forced-parameterization/top-or-paging-literal";
    public const string ForcedParameterizationSelectListLiteralRuleId = "silentscan/forced-parameterization/select-list-literal";
    public const string ForcedParameterizationHavingLiteralRuleId = "silentscan/forced-parameterization/having-literal";
    public const string ForcedParameterizationOrderByExpressionLiteralRuleId = "silentscan/forced-parameterization/order-by-expression-literal";
    public const string ForcedParameterizationTableSampleSizeLiteralRuleId = "silentscan/forced-parameterization/table-sample-size-literal";
    public const string ForcedParameterizationDmlOutputListLiteralRuleId = "silentscan/forced-parameterization/dml-output-list-literal";
    public const string ForcedParameterizationConvertStyleCodeLiteralRuleId = "silentscan/forced-parameterization/convert-style-code-literal";
    public const string ForcedParameterizationConstantFoldableExpressionLiteralRuleId = "silentscan/forced-parameterization/constant-foldable-expression-literal";
    public const string ForcedParameterizationGroupByExpressionLiteralRuleId = "silentscan/forced-parameterization/group-by-expression-literal";
    public static string ForcedParameterizationRuleId(ForcedParameterizationFindingKind kind) => kind switch
    {
        ForcedParameterizationFindingKind.LikePatternLiteral => ForcedParameterizationLikePatternLiteralRuleId,
        ForcedParameterizationFindingKind.TopOrPagingLiteral => ForcedParameterizationTopOrPagingLiteralRuleId,
        ForcedParameterizationFindingKind.SelectListLiteral => ForcedParameterizationSelectListLiteralRuleId,
        ForcedParameterizationFindingKind.HavingLiteral => ForcedParameterizationHavingLiteralRuleId,
        ForcedParameterizationFindingKind.OrderByExpressionLiteral => ForcedParameterizationOrderByExpressionLiteralRuleId,
        ForcedParameterizationFindingKind.TableSampleSizeLiteral => ForcedParameterizationTableSampleSizeLiteralRuleId,
        ForcedParameterizationFindingKind.DmlOutputListLiteral => ForcedParameterizationDmlOutputListLiteralRuleId,
        ForcedParameterizationFindingKind.ConvertStyleCodeLiteral => ForcedParameterizationConvertStyleCodeLiteralRuleId,
        ForcedParameterizationFindingKind.ConstantFoldableExpressionLiteral => ForcedParameterizationConstantFoldableExpressionLiteralRuleId,
        ForcedParameterizationFindingKind.GroupByExpressionLiteral => ForcedParameterizationGroupByExpressionLiteralRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    public const string SetOptionQuotedIdentifierOffBlocksIndexedFeatureRuleId = "silentscan/set-option/quoted-identifier-off";
    public const string SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId = "silentscan/set-option/numeric-roundabort-on";
    public const string SetOptionAnsiNullsOffBlocksIndexedFeatureRuleId = "silentscan/set-option/ansi-nulls-off";
    public const string SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId = "silentscan/set-option/ansi-warnings-off";
    public const string SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId = "silentscan/set-option/concat-null-yields-null-off";
    public const string SetOptionAnsiPaddingOffBlocksIndexedFeatureRuleId = "silentscan/set-option/ansi-padding-off";
    public static string SetOptionRuleId(SetOptionFindingKind kind) => kind switch
    {
        SetOptionFindingKind.QuotedIdentifierOffBlocksIndexedFeature => SetOptionQuotedIdentifierOffBlocksIndexedFeatureRuleId,
        SetOptionFindingKind.NumericRoundabortOnBlocksIndexedFeature => SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId,
        SetOptionFindingKind.AnsiNullsOffBlocksIndexedFeature => SetOptionAnsiNullsOffBlocksIndexedFeatureRuleId,
        SetOptionFindingKind.AnsiWarningsOffBlocksIndexedFeature => SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId,
        SetOptionFindingKind.ConcatNullYieldsNullOffBlocksIndexedFeature => SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId,
        SetOptionFindingKind.AnsiPaddingOffBlocksIndexedFeature => SetOptionAnsiPaddingOffBlocksIndexedFeatureRuleId,
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
    public const string Tier1FunctionWrappedColumnRuleId = "silentscan/tier1/function-wrapped-column";
    public const string Tier1CastOrConvertOnColumnRuleId = "silentscan/tier1/cast-or-convert-on-column";
    public const string Tier1ColumnArithmeticRuleId = "silentscan/tier1/column-arithmetic";
    public const string Tier1LeadingWildcardLikeRuleId = "silentscan/tier1/leading-wildcard-like";
    public const string Tier1RegexpPatternNoLiteralPrefixRuleId = "silentscan/tier1/regexp-pattern-no-literal-prefix";
    public const string Tier1CaseFoldOnColumnRuleId = "silentscan/tier1/case-fold-on-column";
    public const string Tier1DateFunctionOnColumnRuleId = "silentscan/tier1/date-function-on-column";
    public const string Tier1CharindexOrLeftOnColumnRuleId = "silentscan/tier1/charindex-or-left-on-column";
    public static string Tier1RuleId(SargabilityFindingKind kind) => kind switch
    {
        SargabilityFindingKind.FunctionWrappedColumn => Tier1FunctionWrappedColumnRuleId,
        SargabilityFindingKind.CastOrConvertOnColumn => Tier1CastOrConvertOnColumnRuleId,
        SargabilityFindingKind.ColumnArithmetic => Tier1ColumnArithmeticRuleId,
        SargabilityFindingKind.LeadingWildcardLike => Tier1LeadingWildcardLikeRuleId,
        SargabilityFindingKind.RegexpPatternNoLiteralPrefix => Tier1RegexpPatternNoLiteralPrefixRuleId,
        SargabilityFindingKind.CaseFoldOnColumn => Tier1CaseFoldOnColumnRuleId,
        SargabilityFindingKind.DateFunctionOnColumn => Tier1DateFunctionOnColumnRuleId,
        SargabilityFindingKind.CharindexOrLeftOnColumn => Tier1CharindexOrLeftOnColumnRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled SargabilityFindingKind."),
    };
    public const string VerdictScanForcedRuleId = "silentscan/verdict/scan-forced";
    public const string VerdictRangeSeekRuleId = "silentscan/verdict/range-seek";
    public const string VerdictSeekPreservedRuleId = "silentscan/verdict/seek-preserved";
    public static string VerdictRuleId(Verdict verdict) => verdict switch
    {
        Verdict.ScanForced => VerdictScanForcedRuleId,
        Verdict.RangeSeek => VerdictRangeSeekRuleId,
        Verdict.SeekPreserved => VerdictSeekPreservedRuleId,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unhandled Verdict."),
    };
}

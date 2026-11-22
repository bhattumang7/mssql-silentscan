using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Reporting.Sarif;

public static class SarifRuleCatalog
{
    public const string ExpressionDerivedRuleId = FindingRuleIds.ExpressionDerivedRuleId;
    public const string WriteLossUnicodeReplacementRuleId = FindingRuleIds.WriteLossUnicodeReplacementRuleId;
    public const string WriteLossApproximateTruncationRuleId = FindingRuleIds.WriteLossApproximateTruncationRuleId;
    public const string WriteLossNumericScaleNarrowingRuleId = FindingRuleIds.WriteLossNumericScaleNarrowingRuleId;
    public const string WriteLossTemporalPrecisionLossRuleId = FindingRuleIds.WriteLossTemporalPrecisionLossRuleId;
    public const string WriteLossLengthTruncationRuleId = FindingRuleIds.WriteLossLengthTruncationRuleId;
    public const string WriteLossTemporalScaleNarrowingRuleId = FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId;
    public const string WriteLossTemporalOffsetDroppedRuleId = FindingRuleIds.WriteLossTemporalOffsetDroppedRuleId;
    public const string WriteLossSmallDateTimeRoundingRuleId = FindingRuleIds.WriteLossSmallDateTimeRoundingRuleId;
    public const string TvfFenceCorrelatedApplyRuleId = FindingRuleIds.TvfFenceCorrelatedApplyRuleId;
    public const string TvfFenceNestedUnderViewOrTvfRuleId = FindingRuleIds.TvfFenceNestedUnderViewOrTvfRuleId;
    public const string ScalarUdfPredicateInvocationRuleId = FindingRuleIds.ScalarUdfPredicateInvocationRuleId;
    public const string ScalarUdfNestedUnderViewOrTvfRuleId = FindingRuleIds.ScalarUdfNestedUnderViewOrTvfRuleId;
    public const string ScalarUdfSchemaDependencyRuleId = FindingRuleIds.ScalarUdfSchemaDependencyRuleId;
    public const string ScalarUdfProjectionInvocationRuleId = FindingRuleIds.ScalarUdfProjectionInvocationRuleId;
    public const string ColumnAnsiPaddingOffRuleId = FindingRuleIds.ColumnAnsiPaddingOffRuleId;
    public const string ProcCallArgumentMismatchRuleId = FindingRuleIds.ProcCallArgumentMismatchRuleId;
    public const string TvfCallArgumentMismatchRuleId = FindingRuleIds.TvfCallArgumentMismatchRuleId;
    public const string ProcCallTableValuedArgumentMismatchRuleId = FindingRuleIds.ProcCallTableValuedArgumentMismatchRuleId;
    public const string SpExecuteSqlParameterMismatchRuleId = FindingRuleIds.SpExecuteSqlParameterMismatchRuleId;
    public const string TemporalBoundaryPrecisionRuleId = FindingRuleIds.TemporalBoundaryPrecisionRuleId;
    public static string MaxTypedColumnRuleId(NonIndexableColumnFindingKind kind) => FindingRuleIds.MaxTypedColumnRuleId(kind);
    public static string DynamicDataMaskingRuleId(DynamicDataMaskingFindingKind kind) => FindingRuleIds.DynamicDataMaskingRuleId(kind);
    public const string FloatEqualityRuleId = FindingRuleIds.FloatEqualityRuleId;
    public const string FloatOrderDependentAggregateRuleId = FindingRuleIds.FloatOrderDependentAggregateRuleId;
    public const string IsNullReplacementValueTruncationRuleId = FindingRuleIds.IsNullReplacementValueTruncationRuleId;
    public const string MemoryOptimizedSchemaOnlyDurabilityRuleId = FindingRuleIds.MemoryOptimizedSchemaOnlyDurabilityRuleId;
    public const string QueryAntiPatternTableVariableLowCompatEstimateRuleId = FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId;
    public const string QueryAntiPatternTableVariablePspSkipRuleId = FindingRuleIds.QueryAntiPatternTableVariablePspSkipRuleId;
    public const string QueryAntiPatternTableVariableStaleEstimateInLoopRuleId = FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId;
    public const string QueryAntiPatternRbarSingleRowLoopDmlRuleId = FindingRuleIds.QueryAntiPatternRbarSingleRowLoopDmlRuleId;
    public const string QueryAntiPatternCountStarVariableExistenceCheckRuleId = FindingRuleIds.QueryAntiPatternCountStarVariableExistenceCheckRuleId;
    public const string QueryAntiPatternDistinctMaskingJoinFanoutRuleId = FindingRuleIds.QueryAntiPatternDistinctMaskingJoinFanoutRuleId;
    public const string QueryAntiPatternUnqualifiedTableReferenceRuleId = FindingRuleIds.QueryAntiPatternUnqualifiedTableReferenceRuleId;
    public const string QueryAntiPatternMergeMissingHoldlockRuleId = FindingRuleIds.QueryAntiPatternMergeMissingHoldlockRuleId;
    public const string QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId = FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId;
    public const string IndexCoverageKeyLookupProneIndexRuleId = FindingRuleIds.IndexCoverageKeyLookupProneIndexRuleId;
    public const string TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId = FindingRuleIds.TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId;
    public const string TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId = FindingRuleIds.TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId;
    public const string TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId = FindingRuleIds.TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId;
    public const string TriggerCorrectnessUpdateFunctionWithoutValueComparisonRuleId = FindingRuleIds.TriggerCorrectnessUpdateFunctionWithoutValueComparisonRuleId;
    public const string CrossModuleLockOrderRuleId = FindingRuleIds.CrossModuleLockOrderRuleId;
    public const string UnderLengthParameterRuleId = FindingRuleIds.UnderLengthParameterRuleId;
    public const string AnsiPaddingMismatchRuleId = FindingRuleIds.AnsiPaddingMismatchRuleId;
    public const string CatchAllPredicateRuleId = FindingRuleIds.CatchAllPredicateRuleId;
    public const string LocalVariablePredicateRuleId = FindingRuleIds.LocalVariablePredicateRuleId;
    public const string FilteredIndexParameterMismatchRuleId = FindingRuleIds.FilteredIndexParameterMismatchRuleId;
    public const string ParameterReassignmentPredicateRuleId = FindingRuleIds.ParameterReassignmentPredicateRuleId;
    public const string DeprecatedSyntaxEqualsNullComparisonRuleId = FindingRuleIds.DeprecatedSyntaxEqualsNullComparisonRuleId;
    public const string DeprecatedSyntaxNotEqualsNullComparisonRuleId = FindingRuleIds.DeprecatedSyntaxNotEqualsNullComparisonRuleId;
    public const string ControlFlowRiskDirtyReadIsolationHintRuleId = FindingRuleIds.ControlFlowRiskDirtyReadIsolationHintRuleId;
    public const string ControlFlowRiskReadCommittedLockRevertsRowVersioningRuleId = FindingRuleIds.ControlFlowRiskReadCommittedLockRevertsRowVersioningRuleId;
    public const string ControlFlowRiskLegacyIdentityIntrinsicRuleId = FindingRuleIds.ControlFlowRiskLegacyIdentityIntrinsicRuleId;
    public const string ControlFlowRiskCaseExpressionMissingElseRuleId = FindingRuleIds.ControlFlowRiskCaseExpressionMissingElseRuleId;
    public const string ControlFlowRiskNonDeterministicCaseInputRuleId = FindingRuleIds.ControlFlowRiskNonDeterministicCaseInputRuleId;
    public const string NotInNullableSubqueryRuleId = FindingRuleIds.NotInNullableSubqueryRuleId;
    public const string NonUniqueUpdateSourceRuleId = FindingRuleIds.NonUniqueUpdateSourceRuleId;
    public const string CheckConstraintPredicateContradictionIntervalRuleId = FindingRuleIds.CheckConstraintPredicateContradictionIntervalRuleId;
    public const string NotNullPredicateContradictionRuleId = FindingRuleIds.NotNullPredicateContradictionRuleId;
    public static string CheckConstraintPredicateContradictionRuleId(CheckConstraintPredicateContradictionKind kind) => FindingRuleIds.CheckConstraintPredicateContradictionRuleId(kind);
    public const string ForcedSerialTableVariableModificationRuleId = FindingRuleIds.ForcedSerialTableVariableModificationRuleId;
    public const string ForcedSerialFastForwardCursorRuleId = FindingRuleIds.ForcedSerialFastForwardCursorRuleId;
    public const string ForcedSerialNonParallelizableIntrinsicRuleId = FindingRuleIds.ForcedSerialNonParallelizableIntrinsicRuleId;
    public const string UntrustedCheckConstraintRuleId = FindingRuleIds.UntrustedCheckConstraintRuleId;
    public const string MultiReferencedCteRuleId = FindingRuleIds.MultiReferencedCteRuleId;
    public const string PartialCompositeForeignKeyJoinRuleId = FindingRuleIds.PartialCompositeForeignKeyJoinRuleId;
    public const string OuterJoinPredicateCollapseRuleId = FindingRuleIds.OuterJoinPredicateCollapseRuleId;
    public const string ConcatenatedValueInConstantSqlRuleId = FindingRuleIds.ConcatenatedValueInConstantSqlRuleId;
    public const string ExecStringConcatenatesParameterizableValueRuleId = FindingRuleIds.ExecStringConcatenatesParameterizableValueRuleId;
    public const string TempTableExecShapeColumnTypeMismatchRuleId = FindingRuleIds.TempTableExecShapeColumnTypeMismatchRuleId;
    public const string ExecResultSetsShapeColumnTypeMismatchRuleId = FindingRuleIds.ExecResultSetsShapeColumnTypeMismatchRuleId;
    public const string SelfReferencingDmlRuleId = FindingRuleIds.SelfReferencingDmlRuleId;
    public const string TemporalTableHistoryIndexGapRuleId = FindingRuleIds.TemporalTableHistoryIndexGapRuleId;
    public const string CheckConstraintNullNotHandledRuleId = FindingRuleIds.CheckConstraintNullNotHandledRuleId;
    public const string StaleSelectStarViewRuleId = FindingRuleIds.StaleSelectStarViewRuleId;
    public const string BareTopNoOrderByRuleId = FindingRuleIds.BareTopNoOrderByRuleId;
    public const string StringAggMissingOrderRuleId = FindingRuleIds.StringAggMissingOrderRuleId;
    public const string ForXmlPathMissingOrderRuleId = FindingRuleIds.ForXmlPathMissingOrderRuleId;
    public const string JsonArrayAggMissingOrderRuleId = FindingRuleIds.JsonArrayAggMissingOrderRuleId;
    public const string JsonObjectDuplicateKeyRuleId = FindingRuleIds.JsonObjectDuplicateKeyRuleId;
    public const string UnistrUnpairedSurrogateRuleId = FindingRuleIds.UnistrUnpairedSurrogateRuleId;
    public const string RegexpReplaceDollarBackreferenceRuleId = FindingRuleIds.RegexpReplaceDollarBackreferenceRuleId;
    public const string RegexpDefaultCaseSensitiveOnCiColumnRuleId = FindingRuleIds.RegexpDefaultCaseSensitiveOnCiColumnRuleId;
    public const string RegexpAccentInsensitiveColumnRuleId = FindingRuleIds.RegexpAccentInsensitiveColumnRuleId;
    public const string StringConcatNullRuleId = FindingRuleIds.StringConcatNullRuleId;
    public const string SecurityPredicateIndexRuleId = FindingRuleIds.SecurityPredicateIndexRuleId;
    public static string ModuleCompileFlagRuleId(ModuleCompileFlagFindingKind kind) => FindingRuleIds.ModuleCompileFlagRuleId(kind);
    public static string WindowFrameRuleId(WindowFrameFindingKind kind) => FindingRuleIds.WindowFrameRuleId(kind);
    public static string BoundedStringBuiltinTruncationRuleId(BoundedStringBuiltinTruncationFindingKind kind) => FindingRuleIds.BoundedStringBuiltinTruncationRuleId(kind);
    public static string TransactionHygieneRuleId(TransactionHygieneFindingKind kind) => FindingRuleIds.TransactionHygieneRuleId(kind);
    public const string OutputParameterRuleId = FindingRuleIds.OutputParameterRuleId;
    public const string CompositeIndexLeadingColumnRuleId = FindingRuleIds.CompositeIndexLeadingColumnRuleId;
    public const string MissingStatisticsRuleId = FindingRuleIds.MissingStatisticsRuleId;
    public static string IndexHintRuleId(IndexHintFindingKind kind) => FindingRuleIds.IndexHintRuleId(kind);
    public const string AmbiguousDateLiteralConversionRuleId = FindingRuleIds.AmbiguousDateLiteralConversionRuleId;
    public static string CartesianJoinRuleId(CartesianJoinKind kind) => FindingRuleIds.CartesianJoinRuleId(kind);
    public const string TruncateSwallowedRuleId = FindingRuleIds.TruncateSwallowedRuleId;
    public static string DatabaseConfigurationRuleId(DatabaseConfigurationFindingKind kind) => FindingRuleIds.DatabaseConfigurationRuleId(kind);
    public static string UnindexedTempTableUsageRuleId(UnindexedTempTableUsageKind kind) => FindingRuleIds.UnindexedTempTableUsageRuleId(kind);
    public static string ViewOrderingRuleId(ViewOrderingFindingKind kind) => FindingRuleIds.ViewOrderingRuleId(kind);
    public static string IndexDesignRuleId(IndexDesignFindingKind kind) => FindingRuleIds.IndexDesignRuleId(kind);
    public static string ForcedParameterizationRuleId(ForcedParameterizationFindingKind kind) => FindingRuleIds.ForcedParameterizationRuleId(kind);
    public static string SetOptionRuleId(SetOptionFindingKind kind) => FindingRuleIds.SetOptionRuleId(kind);
    public static string UnparameterizedDynamicSqlRuleId(UnparameterizedDynamicSqlFindingKind kind) => FindingRuleIds.UnparameterizedDynamicSqlRuleId(kind);
    public static string TempTableExecShapeRuleId(TempTableExecShapeFindingKind kind) => FindingRuleIds.TempTableExecShapeRuleId(kind);
    public static string ExecResultSetsShapeRuleId(ExecResultSetsShapeFindingKind kind) => FindingRuleIds.ExecResultSetsShapeRuleId(kind);
    public static string ForcedSerialRuleId(ForcedSerialFindingKind kind) => FindingRuleIds.ForcedSerialRuleId(kind);
    public static string DeprecatedSyntaxRuleId(DeprecatedSyntaxFindingKind kind) => FindingRuleIds.DeprecatedSyntaxRuleId(kind);
    public static string ControlFlowRiskRuleId(ControlFlowRiskFindingKind kind) => FindingRuleIds.ControlFlowRiskRuleId(kind);
    public static string QueryAntiPatternRuleId(QueryAntiPatternFindingKind kind) => FindingRuleIds.QueryAntiPatternRuleId(kind);
    public static string IndexCoverageRuleId(IndexCoverageFindingKind kind) => FindingRuleIds.IndexCoverageRuleId(kind);
    public static string TriggerCorrectnessRuleId(TriggerCorrectnessFindingKind kind) => FindingRuleIds.TriggerCorrectnessRuleId(kind);
    public static string UntrustedConstraintRuleId(UntrustedConstraintFindingKind kind) => FindingRuleIds.UntrustedConstraintRuleId(kind);
    public static string CheckConstraintRuleId(CheckConstraintFindingKind kind) => FindingRuleIds.CheckConstraintRuleId(kind);
    public static string TvfFenceRuleId(TvfFenceFindingKind kind) => FindingRuleIds.TvfFenceRuleId(kind);
    public static string ScalarUdfRuleId(ScalarUdfFindingKind kind) => FindingRuleIds.ScalarUdfRuleId(kind);
    public static string WriteLossRuleId(WriteLossKind kind) => FindingRuleIds.WriteLossRuleId(kind);
    public static string Tier1RuleId(SargabilityFindingKind kind) => FindingRuleIds.Tier1RuleId(kind);
    public static string VerdictRuleId(Verdict verdict) => FindingRuleIds.VerdictRuleId(verdict);

    public static string RuleId(string baseRuleId, FindingConfidence confidence) => confidence switch
    {
        FindingConfidence.High => baseRuleId,
        FindingConfidence.Medium => $"{baseRuleId}/medium-confidence",
        FindingConfidence.Low => $"{baseRuleId}/low-confidence",
        _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Unhandled FindingConfidence."),
    };

    public static IReadOnlyList<SarifRule> AllRules { get; } = BuildAllRules();

    private static IReadOnlyList<SarifRule> BuildAllRules()
    {
        var baseRules = RuleCatalog.BaseRules.Select(rule => Rule(rule.Id, rule.Rationale)).ToArray();

        var mediumVariants = baseRules
            .Select(rule => Rule(RuleId(rule.Id, FindingConfidence.Medium), $"(Medium confidence) {rule.ShortDescription.Text}"));
        var lowVariants = baseRules
            .Select(rule => Rule(RuleId(rule.Id, FindingConfidence.Low), $"(Low confidence) {rule.ShortDescription.Text}"));

        return [.. baseRules, .. mediumVariants, .. lowVariants];
    }

    private static SarifRule Rule(string id, string description) => new(id, new SarifMessage(description), RuleDocSite.Url(id));
}

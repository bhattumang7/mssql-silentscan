using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Support;

public static class TestScanReports
{
    public static ScanReport Build(
        ParseHealthReport? ParseHealth = null,
        IReadOnlyList<SargabilityFinding>? Tier1Findings = null,
        IReadOnlyList<TypedPredicateFinding>? TypedFindings = null,
        IReadOnlyList<DynamicSqlFinding>? DynamicSqlFindings = null,
        IReadOnlyList<ExpressionDerivedFinding>? ExpressionDerivedFindings = null,
        IReadOnlyList<WriteLossFinding>? WriteLossFindings = null,
        IReadOnlyList<TvfFenceFinding>? TvfFenceFindings = null,
        IReadOnlyList<ScalarUdfFinding>? ScalarUdfFindings = null,
        IReadOnlyList<AnsiPaddingOffColumnFinding>? AnsiPaddingOffColumnFindings = null,
        IReadOnlyList<ProcCallArgumentMismatchFinding>? ProcCallArgumentMismatchFindings = null,
        IReadOnlyList<TvfCallArgumentMismatchFinding>? TvfCallArgumentMismatchFindings = null,
        IReadOnlyList<ProcCallTableValuedArgumentMismatchFinding>? ProcCallTableValuedArgumentMismatchFindings = null,
        IReadOnlyList<TemporalBoundaryPrecisionFinding>? TemporalBoundaryFindings = null,
        IReadOnlyList<MaxTypedColumnFinding>? MaxTypedColumnFindings = null,
        IReadOnlyList<UnderLengthParameterFinding>? UnderLengthParameterFindings = null,
        IReadOnlyList<AnsiPaddingMismatchFinding>? AnsiPaddingMismatchFindings = null,
        IReadOnlyList<PartialCompositeForeignKeyJoinFinding>? PartialCompositeForeignKeyJoinFindings = null,
        IReadOnlyList<SetOptionFinding>? SetOptionFindings = null,
        IReadOnlyList<CatchAllPredicateFinding>? CatchAllPredicateFindings = null,
        IReadOnlyList<LocalVariablePredicateFinding>? LocalVariablePredicateFindings = null,
        IReadOnlyList<FilteredIndexParameterMismatchFinding>? FilteredIndexParameterMismatchFindings = null,
        IReadOnlyList<NotInNullableSubqueryFinding>? NotInNullableSubqueryFindings = null,
        IReadOnlyList<NonUniqueUpdateSourceFinding>? NonUniqueUpdateSourceFindings = null,
        IReadOnlyList<ForcedSerialFinding>? ForcedSerialFindings = null,
        IReadOnlyList<UntrustedConstraintFinding>? UntrustedConstraintFindings = null,
        IReadOnlyList<MultiReferencedCteFinding>? MultiReferencedCteFindings = null,
        IReadOnlyList<UnparameterizedDynamicSqlFinding>? UnparameterizedDynamicSqlFindings = null,
        IReadOnlyList<TempTableExecShapeFinding>? TempTableExecShapeFindings = null,
        IReadOnlyList<ExecResultSetsShapeFinding>? ExecResultSetsShapeFindings = null,
        IReadOnlyList<SelfReferencingDmlFinding>? SelfReferencingDmlFindings = null,
        IReadOnlyList<TemporalTableHistoryIndexGapFinding>? TemporalTableHistoryIndexGapFindings = null,
        IReadOnlyList<ModuleCompileFlagFinding>? ModuleCompileFlagFindings = null,
        IReadOnlyList<WindowFrameFinding>? WindowFrameFindings = null,
        IReadOnlyList<ViewOrderingFinding>? ViewOrderingFindings = null,
        IReadOnlyList<TransactionHygieneFinding>? TransactionHygieneFindings = null,
        IReadOnlyList<CompositeIndexLeadingColumnFinding>? CompositeIndexLeadingColumnFindings = null,
        IReadOnlyList<IndexHintFinding>? IndexHintFindings = null,
        IReadOnlyList<AmbiguousDateLiteralConversionFinding>? AmbiguousDateLiteralConversionFindings = null,
        IReadOnlyList<CartesianJoinFinding>? CartesianJoinFindings = null,
        IReadOnlyList<TruncateSwallowedFinding>? TruncateSwallowedFindings = null,
        IReadOnlyList<UnindexedTempTableUsageFinding>? UnindexedTempTableUsageFindings = null,
        IReadOnlyList<OutputParameterFinding>? OutputParameterFindings = null,
        IReadOnlyList<DatabaseConfigurationFinding>? DatabaseConfigurationFindings = null,
        IReadOnlyList<ParameterReassignmentPredicateFinding>? ParameterReassignmentPredicateFindings = null,
        IReadOnlyList<DeprecatedSyntaxFinding>? DeprecatedSyntaxFindings = null,
        IReadOnlyList<ControlFlowRiskFinding>? ControlFlowRiskFindings = null,
        IReadOnlyList<IndexDesignFinding>? IndexDesignFindings = null,
        IReadOnlyList<FloatEqualityFinding>? FloatEqualityFindings = null,
        IReadOnlyList<QueryAntiPatternFinding>? QueryAntiPatternFindings = null,
        IReadOnlyList<IndexCoverageFinding>? IndexCoverageFindings = null,
        IReadOnlyList<TriggerCorrectnessFinding>? TriggerCorrectnessFindings = null,
        IReadOnlyList<CheckConstraintFinding>? CheckConstraintFindings = null,
        IReadOnlyList<CheckConstraintPredicateContradictionFinding>? CheckConstraintPredicateContradictionFindings = null,
        IReadOnlyList<StaleSelectStarViewFinding>? StaleSelectStarViewFindings = null,
        IReadOnlyList<BareTopNoOrderByFinding>? BareTopNoOrderByFindings = null,
        IReadOnlyList<StringAggMissingOrderFinding>? StringAggMissingOrderFindings = null,
        IReadOnlyList<ForXmlPathMissingOrderFinding>? ForXmlPathMissingOrderFindings = null,
        IReadOnlyList<JsonArrayAggMissingOrderFinding>? JsonArrayAggMissingOrderFindings = null,
        IReadOnlyList<JsonObjectDuplicateKeyFinding>? JsonObjectDuplicateKeyFindings = null,
        IReadOnlyList<UnistrUnpairedSurrogateFinding>? UnistrUnpairedSurrogateFindings = null,
        IReadOnlyList<RegexpReplaceDollarBackreferenceFinding>? RegexpReplaceDollarBackreferenceFindings = null,
        IReadOnlyList<RegexpDefaultCaseSensitiveOnCiColumnFinding>? RegexpDefaultCaseSensitiveOnCiColumnFindings = null,
        IReadOnlyList<RegexpAccentInsensitiveColumnFinding>? RegexpAccentInsensitiveColumnFindings = null,
        IReadOnlyList<StringConcatNullFinding>? StringConcatNullFindings = null,
        IReadOnlyList<SecurityPredicateIndexFinding>? SecurityPredicateIndexFindings = null,
        IReadOnlyList<ForcedParameterizationFinding>? ForcedParameterizationFindings = null,
        IReadOnlyList<MissingStatisticsFinding>? MissingStatisticsFindings = null,
        IReadOnlyList<MemoryOptimizedSchemaOnlyDurabilityFinding>? MemoryOptimizedSchemaOnlyDurabilityFindings = null,
        IReadOnlyList<BoundedStringBuiltinTruncationFinding>? BoundedStringBuiltinTruncationFindings = null,
        IReadOnlyList<FloatOrderDependentAggregateFinding>? FloatOrderDependentAggregateFindings = null,
        IReadOnlyList<DynamicDataMaskingFinding>? DynamicDataMaskingFindings = null,
        IReadOnlyList<SpExecuteSqlParameterMismatchFinding>? SpExecuteSqlParameterMismatchFindings = null,
        IReadOnlyList<SkippedConstruct>? SkippedConstructs = null,
        SkippedConstructSummary? SkippedConstructSummary = null,
        TypedPredicateSummary? TypedPredicateSummary = null,
        DynamicSqlSummary? DynamicSqlSummary = null,
        int SchemaVersion = ScanReport.CurrentSchemaVersion)
    {
        var findingsByRuleId = new Dictionary<string, IReadOnlyList<IFinding>>(StringComparer.Ordinal);
        void Set<TFinding>(string ruleId, IReadOnlyList<TFinding>? findings)
            where TFinding : IFinding
        {
            if (findings is { Count: > 0 })
            {
                findingsByRuleId[ruleId] = [.. findingsByRuleId.TryGetValue(ruleId, out var existing) ? existing : [], .. findings.Cast<IFinding>()];
            }
        }

        Set("NonSargablePredicateScanner", Tier1Findings);
        Set("NonSargablePredicateScanner", TemporalBoundaryFindings);
        Set("TypedPredicateExtractor", TypedFindings);
        Set("TypedPredicateExtractor", ExpressionDerivedFindings);
        Set("TypedPredicateExtractor", WriteLossFindings);
        Set("TypedPredicateExtractor", UnderLengthParameterFindings);
        Set("TypedPredicateExtractor", AnsiPaddingMismatchFindings);
        Set("TypedPredicateExtractor", LocalVariablePredicateFindings);
        Set("TypedPredicateExtractor", FilteredIndexParameterMismatchFindings);
        Set("DynamicSqlScanner", UnparameterizedDynamicSqlFindings);
        Set("TvfFenceScanner", TvfFenceFindings);
        Set("ScalarUdfScanner", ScalarUdfFindings);
        Set("AnsiPaddingOffColumnScanner", AnsiPaddingOffColumnFindings);
        Set("ProcCallArgumentMismatchScanner", ProcCallArgumentMismatchFindings);
        Set("TvfCallArgumentMismatchScanner", TvfCallArgumentMismatchFindings);
        Set("ProcCallTableValuedArgumentMismatchScanner", ProcCallTableValuedArgumentMismatchFindings);
        Set("MaxTypedColumnScanner", MaxTypedColumnFindings);
        Set("PartialCompositeForeignKeyJoinScanner", PartialCompositeForeignKeyJoinFindings);
        Set("SetOptionScanner", SetOptionFindings);
        Set("CatchAllPredicateScanner", CatchAllPredicateFindings);
        Set("NotInNullableSubqueryScanner", NotInNullableSubqueryFindings);
        Set("NonUniqueUpdateSourceScanner", NonUniqueUpdateSourceFindings);
        Set("ForcedSerialScanner", ForcedSerialFindings);
        Set("UntrustedConstraintScanner", UntrustedConstraintFindings);
        Set("MultiReferencedCteScanner", MultiReferencedCteFindings);
        Set("TempTableExecShapeScanner", TempTableExecShapeFindings);
        Set("ExecResultSetsShapeScanner", ExecResultSetsShapeFindings);
        Set("SelfReferencingDmlScanner", SelfReferencingDmlFindings);
        Set("TemporalTableHistoryIndexGapScanner", TemporalTableHistoryIndexGapFindings);
        Set("ModuleCompileFlagScanner", ModuleCompileFlagFindings);
        Set("WindowFrameScanner", WindowFrameFindings);
        Set("ViewOrderingScanner", ViewOrderingFindings);
        Set("TransactionHygieneScanner", TransactionHygieneFindings);
        Set("CompositeIndexLeadingColumnScanner", CompositeIndexLeadingColumnFindings);
        Set("IndexHintScanner", IndexHintFindings);
        Set("AmbiguousDateLiteralConversionScanner", AmbiguousDateLiteralConversionFindings);
        Set("CartesianJoinScanner", CartesianJoinFindings);
        Set("TruncateSwallowedScanner", TruncateSwallowedFindings);
        Set("UnindexedTempTableUsageScanner", UnindexedTempTableUsageFindings);
        Set("OutputParameterScanner", OutputParameterFindings);
        Set("DatabaseConfigurationScanner", DatabaseConfigurationFindings);
        Set("ParameterReassignmentPredicateScanner", ParameterReassignmentPredicateFindings);
        Set("DeprecatedSyntaxScanner", DeprecatedSyntaxFindings);
        Set("ControlFlowRiskScanner", ControlFlowRiskFindings);
        Set("IndexDesignScanner", IndexDesignFindings);
        Set("FloatEqualityPredicateScanner", FloatEqualityFindings);
        Set("QueryAntiPatternScanner", QueryAntiPatternFindings);
        Set("IndexCoverageScanner", IndexCoverageFindings);
        Set("TriggerCorrectnessScanner", TriggerCorrectnessFindings);
        Set("CheckConstraintScanner", CheckConstraintFindings);
        Set(nameof(CheckConstraintPredicateContradictionScanner), CheckConstraintPredicateContradictionFindings);
        Set("StaleSelectStarViewScanner", StaleSelectStarViewFindings);
        Set("BareTopNoOrderByScanner", BareTopNoOrderByFindings);
        Set("StringAggMissingOrderScanner", StringAggMissingOrderFindings);
        Set("ForXmlPathMissingOrderScanner", ForXmlPathMissingOrderFindings);
        Set("JsonArrayAggMissingOrderScanner", JsonArrayAggMissingOrderFindings);
        Set("JsonObjectDuplicateKeyScanner", JsonObjectDuplicateKeyFindings);
        Set("UnistrUnpairedSurrogateScanner", UnistrUnpairedSurrogateFindings);
        Set("RegexpReplaceDollarBackreferenceScanner", RegexpReplaceDollarBackreferenceFindings);
        Set("RegexpDefaultCaseSensitiveOnCiColumnScanner", RegexpDefaultCaseSensitiveOnCiColumnFindings);
        Set("RegexpAccentInsensitiveColumnScanner", RegexpAccentInsensitiveColumnFindings);
        Set("StringConcatNullScanner", StringConcatNullFindings);
        Set("SecurityPredicateIndexScanner", SecurityPredicateIndexFindings);
        Set("ForcedParameterizationScanner", ForcedParameterizationFindings);
        Set("MissingStatisticsScanner", MissingStatisticsFindings);
        Set("MemoryOptimizedSchemaOnlyDurabilityScanner", MemoryOptimizedSchemaOnlyDurabilityFindings);
        Set("BoundedStringBuiltinTruncationScanner", BoundedStringBuiltinTruncationFindings);
        Set("FloatOrderDependentAggregateScanner", FloatOrderDependentAggregateFindings);
        Set(nameof(DynamicDataMaskingScanner), DynamicDataMaskingFindings);
        Set("SpExecuteSqlParameterMismatchScanner", SpExecuteSqlParameterMismatchFindings);

        return new ScanReport(
            ParseHealth ?? new ParseHealthReport([]),
            findingsByRuleId,
            SkippedConstructs ?? [],
            SkippedConstructSummary ?? SkippedConstructSummary.From([]),
            TypedPredicateSummary ?? TypedPredicateSummary.From([]),
            DynamicSqlSummary ?? DynamicSqlSummary.From([]),
            DynamicSqlFindings ?? [],
            SchemaVersion);
    }
}

using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting.RuleHarness;

public sealed class DefaultLocationComparer : IComparer<IFinding>
{
    public static readonly DefaultLocationComparer Instance = new();

    public int Compare(IFinding? x, IFinding? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var pathCompare = string.CompareOrdinal(x.Location.SourcePath, y.Location.SourcePath);
        if (pathCompare != 0)
        {
            return pathCompare;
        }

        var lineCompare = x.Location.Line.CompareTo(y.Location.Line);
        return lineCompare != 0 ? lineCompare : x.Location.Column.CompareTo(y.Location.Column);
    }
}

public sealed class RuleRunResult(
    IReadOnlyDictionary<string, IReadOnlyList<IFinding>> findingsByRuleId,
    IReadOnlyList<SkippedConstruct> crashes)
{
    public IReadOnlyList<SkippedConstruct> Crashes { get; } = crashes;

    public IReadOnlyDictionary<string, IReadOnlyList<IFinding>> AllFindings => findingsByRuleId;

    public IReadOnlyList<TFinding> For<TFinding>(string ruleId)
        where TFinding : IFinding =>
        findingsByRuleId.TryGetValue(ruleId, out var findings)
            ? [.. findings.OfType<TFinding>()]
            : [];
}

public static class RuleRunner
{
    private const string RuleCrashKind = "RuleCrash";

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static RuleRunResult Run(
        IReadOnlyList<IRule> rules,
        IReadOnlyList<SqlParseResult> parseResults,
        RuleContext context,
        FindingConfidence minimumConfidence,
        IScanProgress progress)
    {
        var resultsByRuleId = new Dictionary<string, IReadOnlyList<IFinding>>(StringComparer.Ordinal);
        var crashes = new List<SkippedConstruct>();

        var perFileRules = rules.OfType<IPerFileRule>().ToList();
        var rawPerFileFindings = RunPerFileRules(perFileRules, parseResults, context, progress, crashes);

        var crossModuleRules = rules.OfType<ICrossModuleRule>().ToList();
        var rawCrossModuleFindings = RunCrossModuleRules(crossModuleRules, parseResults, context, crashes);

        foreach (var rule in rules)
        {
            IReadOnlyList<IFinding> raw = rule switch
            {
                IPerFileRule perFileRule => rawPerFileFindings[perFileRule.Id],
                ICatalogRule catalogRule => RunCatalogRule(catalogRule, context, crashes),
                ICrossModuleRule crossModuleRule => rawCrossModuleFindings[crossModuleRule.Id],
                _ => throw new InvalidOperationException($"Rule '{rule.Id}' does not implement IPerFileRule, ICatalogRule, or ICrossModuleRule."),
            };

            resultsByRuleId[rule.Id] = FinalizeRule(rule, raw, minimumConfidence);
        }

        return new RuleRunResult(resultsByRuleId, crashes);
    }

    public static IReadOnlyList<IFinding> FinalizeRule(IRule rule, IEnumerable<IFinding> raw, FindingConfidence minimumConfidence)
    {
        var comparer = (rule as IPerFileRule)?.Comparer ?? DefaultLocationComparer.Instance;
        var filtered = rule.ApplyConfidenceFilter ? raw.Where(f => f.Confidence <= minimumConfidence) : raw;
        return [.. filtered.OrderBy(f => f, comparer)];
    }

    private sealed record BatchState(
        Dictionary<IPerFileRule, object?> StateByRule,
        Dictionary<string, List<IFinding>> Results,
        List<SkippedConstruct> Crashes);

    public static Dictionary<string, List<IFinding>> RunOnBatch(
        IReadOnlyList<IPerFileRule> rules,
        SqlParseResult innerParseResult,
        RuleContext context,
        ModuleWalkerCallerContext callerContext,
        List<SkippedConstruct> crashes)
    {
        var results = new Dictionary<string, List<IFinding>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            results[rule.Id] = [];
        }

        var stateByRule = PrepareRules(rules, context, innerParseResult.SourcePath, crashes, out var preparedRules);
        var state = new BatchState(stateByRule, results, crashes);

        var moduleRuleOwner = new Dictionary<IModuleRule, IPerFileRule>();
        var moduleRules = ResolveModuleRules(preparedRules, innerParseResult, context, state, moduleRuleOwner);

        if (moduleRules.Count > 0)
        {
            RunModuleRules(moduleRules, moduleRuleOwner, innerParseResult, context, state, callerContext);
        }

        ScanCatalogOnceForAll(preparedRules, context, state, innerParseResult.SourcePath);

        return results;
    }

    private static Dictionary<IPerFileRule, object?> PrepareRules(
        IReadOnlyList<IPerFileRule> rules,
        RuleContext context,
        string sourcePath,
        List<SkippedConstruct> crashes,
        out List<IPerFileRule> preparedRules)
    {
        var stateByRule = new Dictionary<IPerFileRule, object?>();
        preparedRules = [];
        foreach (var rule in rules)
        {
            try
            {
                stateByRule[rule] = rule.Prepare(context);
                preparedRules.Add(rule);
            }
            catch (Exception ex)
            {
                RecordCrash(crashes, rule.Id, sourcePath, ex);
            }
        }

        return stateByRule;
    }

    private static List<IModuleRule> ResolveModuleRules(
        List<IPerFileRule> preparedRules,
        SqlParseResult innerParseResult,
        RuleContext context,
        BatchState state,
        Dictionary<IModuleRule, IPerFileRule> moduleRuleOwner)
    {
        var moduleRules = new List<IModuleRule>();
        foreach (var rule in preparedRules)
        {
            IModuleRule? moduleRule;
            try
            {
                moduleRule = rule.CreateModuleRule(innerParseResult, context, state.StateByRule[rule]);
            }
            catch (Exception ex)
            {
                RecordCrash(state.Crashes, rule.Id, innerParseResult.SourcePath, ex);
                continue;
            }

            if (moduleRule is null)
            {
                state.Results[rule.Id].AddRange(ScanLegacy(rule, innerParseResult, context, state.StateByRule[rule], state.Crashes));
                continue;
            }

            moduleRules.Add(moduleRule);
            moduleRuleOwner[moduleRule] = rule;
        }

        return moduleRules;
    }

    private static IReadOnlyList<IFinding> ScanLegacy(
        IPerFileRule rule, SqlParseResult innerParseResult, RuleContext context, object? state, List<SkippedConstruct> crashes)
    {
        try
        {
            return rule.Scan(innerParseResult, context, state);
        }
        catch (Exception ex)
        {
            RecordCrash(crashes, rule.Id, innerParseResult.SourcePath, ex);
            return [];
        }
    }

    private static void RunModuleRules(
        List<IModuleRule> moduleRules,
        Dictionary<IModuleRule, IPerFileRule> moduleRuleOwner,
        SqlParseResult innerParseResult,
        RuleContext context,
        BatchState state,
        ModuleWalkerCallerContext callerContext)
    {
        var walker = new ModuleWalker(
            innerParseResult.SourcePath, context.Catalog, EmptyResolvedViews, rules: moduleRules, callerContext: callerContext);
        innerParseResult.Fragment.Accept(walker);

        foreach (var moduleRule in moduleRules)
        {
            var rule = moduleRuleOwner[moduleRule];
            if (walker.CrashedRules.TryGetValue(moduleRule, out var crashException))
            {
                RecordCrash(state.Crashes, rule.Id, innerParseResult.SourcePath, crashException);
                continue;
            }

            IReadOnlyList<IFinding> harvested;
            try
            {
                harvested = rule.HarvestFindings(innerParseResult, context, state.StateByRule[rule], moduleRule);
            }
            catch (Exception ex)
            {
                RecordCrash(state.Crashes, rule.Id, innerParseResult.SourcePath, ex);
                harvested = [];
            }

            state.Results[rule.Id].AddRange(harvested);
        }
    }

    private static void ScanCatalogOnceForAll(
        List<IPerFileRule> preparedRules,
        RuleContext context,
        BatchState state,
        string sourcePath)
    {
        foreach (var rule in preparedRules)
        {
            try
            {
                state.Results[rule.Id].AddRange(rule.ScanCatalogOnce(context));
            }
            catch (Exception ex)
            {
                RecordCrash(state.Crashes, rule.Id, sourcePath, ex);
            }
        }
    }

    private static Dictionary<string, List<IFinding>> RunPerFileRules(
        IReadOnlyList<IPerFileRule> rules,
        IReadOnlyList<SqlParseResult> parseResults,
        RuleContext context,
        IScanProgress progress,
        List<SkippedConstruct> crashes)
    {
        var findingsByRuleId = new Dictionary<string, List<IFinding>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            findingsByRuleId[rule.Id] = [];
        }

        var stateByRule = new Dictionary<IPerFileRule, object?>();
        var preparedRules = new List<IPerFileRule>();
        foreach (var rule in rules)
        {
            try
            {
                stateByRule[rule] = rule.Prepare(context);
                preparedRules.Add(rule);
            }
            catch (Exception ex)
            {
                crashes.Add(new SkippedConstruct(AnalysisPass.Predicates, string.Empty, 0, 0, RuleCrashKind, $"{rule.Id}: {ex.Message}"));
            }
        }

        var stages = preparedRules.ToDictionary(rule => rule, rule => progress.Begin(rule.Id, parseResults.Count));
        try
        {
            var perFileResults = parseResults
                .AsParallel()
                .Select(parseResult => ScanOneFile(preparedRules, parseResult, context, stateByRule, stages, crashes))
                .ToList();

            foreach (var perFile in perFileResults)
            {
                foreach (var (ruleId, findings) in perFile)
                {
                    findingsByRuleId[ruleId].AddRange(findings);
                }
            }
        }
        finally
        {
            foreach (var stage in stages.Values)
            {
                stage.Dispose();
            }
        }

        foreach (var rule in preparedRules)
        {
            try
            {
                findingsByRuleId[rule.Id].AddRange(rule.ScanCatalogOnce(context));
            }
            catch (Exception ex)
            {
                crashes.Add(new SkippedConstruct(AnalysisPass.Predicates, string.Empty, 0, 0, RuleCrashKind, $"{rule.Id}: {ex.Message}"));
            }
        }

        return findingsByRuleId;
    }

    private static List<(string RuleId, IReadOnlyList<IFinding> Findings)> ScanOneFile(
        List<IPerFileRule> rules,
        SqlParseResult parseResult,
        RuleContext context,
        Dictionary<IPerFileRule, object?> stateByRule,
        Dictionary<IPerFileRule, IScanStage> stages,
        List<SkippedConstruct> crashes)
    {
        var results = new List<(string, IReadOnlyList<IFinding>)>(rules.Count);
        var moduleRules = new List<IModuleRule>();
        var moduleRuleOwner = new Dictionary<IModuleRule, IPerFileRule>();

        foreach (var rule in rules)
        {
            stages[rule].Advance(currentItem: parseResult.SourcePath);

            IModuleRule? moduleRule;
            try
            {
                moduleRule = rule.CreateModuleRule(parseResult, context, stateByRule[rule]);
            }
            catch (Exception ex)
            {
                RecordCrash(crashes, rule.Id, parseResult.SourcePath, ex);
                results.Add((rule.Id, []));
                continue;
            }

            if (moduleRule is null)
            {
                IReadOnlyList<IFinding> legacyFindings;
                try
                {
                    legacyFindings = rule.Scan(parseResult, context, stateByRule[rule]);
                }
                catch (Exception ex)
                {
                    RecordCrash(crashes, rule.Id, parseResult.SourcePath, ex);
                    legacyFindings = [];
                }

                results.Add((rule.Id, legacyFindings));
                continue;
            }

            moduleRules.Add(moduleRule);
            moduleRuleOwner[moduleRule] = rule;
        }

        if (moduleRules.Count == 0)
        {
            return results;
        }

        var walker = new ModuleWalker(
            parseResult.SourcePath, context.Catalog, EmptyResolvedViews, rules: moduleRules);
        parseResult.Fragment.Accept(walker);

        foreach (var moduleRule in moduleRules)
        {
            var rule = moduleRuleOwner[moduleRule];
            stages[rule].Advance(currentItem: parseResult.SourcePath);

            if (walker.CrashedRules.TryGetValue(moduleRule, out var crashException))
            {
                RecordCrash(crashes, rule.Id, parseResult.SourcePath, crashException);
                results.Add((rule.Id, []));
                continue;
            }

            IReadOnlyList<IFinding> harvested;
            try
            {
                harvested = rule.HarvestFindings(parseResult, context, stateByRule[rule], moduleRule);
            }
            catch (Exception ex)
            {
                RecordCrash(crashes, rule.Id, parseResult.SourcePath, ex);
                harvested = [];
            }

            results.Add((rule.Id, harvested));
        }

        return results;
    }

    private static void RecordCrash(List<SkippedConstruct> crashes, string ruleId, string sourcePath, Exception ex)
    {
        lock (crashes)
        {
            crashes.Add(new SkippedConstruct(AnalysisPass.Predicates, sourcePath, 0, 0, RuleCrashKind, $"{ruleId}: {ex.Message}"));
        }
    }

    private static IReadOnlyList<IFinding> RunCatalogRule(ICatalogRule rule, RuleContext context, List<SkippedConstruct> crashes)
    {
        try
        {
            return rule.Scan(context);
        }
        catch (Exception ex)
        {
            crashes.Add(new SkippedConstruct(AnalysisPass.Predicates, string.Empty, 0, 0, RuleCrashKind, $"{rule.Id}: {ex.Message}"));
            return [];
        }
    }

    private static Dictionary<string, IReadOnlyList<IFinding>> RunCrossModuleRules(
        IReadOnlyList<ICrossModuleRule> rules,
        IReadOnlyList<SqlParseResult> parseResults,
        RuleContext context,
        List<SkippedConstruct> crashes)
    {
        var moduleRulesByRule = rules.ToDictionary(rule => rule, _ => new List<IModuleRule>());

        var perFileResults = parseResults
            .AsParallel()
            .Select(parseResult => ScanOneFileForCrossModuleRules(rules, parseResult, context, crashes))
            .ToList();

        foreach (var perFile in perFileResults)
        {
            foreach (var (rule, moduleRule) in perFile)
            {
                moduleRulesByRule[rule].Add(moduleRule);
            }
        }

        var findingsByRuleId = new Dictionary<string, IReadOnlyList<IFinding>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var moduleRules = moduleRulesByRule[rule];
            try
            {
                findingsByRuleId[rule.Id] = moduleRules.Count > 0
                    ? rule.Aggregate(context, moduleRules)
                    : rule.Scan(parseResults, context);
            }
            catch (Exception ex)
            {
                crashes.Add(new SkippedConstruct(AnalysisPass.Predicates, string.Empty, 0, 0, RuleCrashKind, $"{rule.Id}: {ex.Message}"));
                findingsByRuleId[rule.Id] = [];
            }
        }

        return findingsByRuleId;
    }

    private static List<(ICrossModuleRule Rule, IModuleRule ModuleRule)> ScanOneFileForCrossModuleRules(
        IReadOnlyList<ICrossModuleRule> rules,
        SqlParseResult parseResult,
        RuleContext context,
        List<SkippedConstruct> crashes)
    {
        var moduleRules = new List<IModuleRule>();
        var moduleRuleOwner = new Dictionary<IModuleRule, ICrossModuleRule>();

        foreach (var rule in rules)
        {
            IModuleRule? moduleRule;
            try
            {
                moduleRule = rule.CreateModuleRule(parseResult, context);
            }
            catch (Exception ex)
            {
                RecordCrash(crashes, rule.Id, parseResult.SourcePath, ex);
                continue;
            }

            if (moduleRule is null)
            {
                continue;
            }

            moduleRules.Add(moduleRule);
            moduleRuleOwner[moduleRule] = rule;
        }

        var results = new List<(ICrossModuleRule, IModuleRule)>(moduleRules.Count);
        if (moduleRules.Count == 0)
        {
            return results;
        }

        var walker = new ModuleWalker(
            parseResult.SourcePath, context.Catalog, EmptyResolvedViews, rules: moduleRules);
        parseResult.Fragment.Accept(walker);

        foreach (var moduleRule in moduleRules)
        {
            var rule = moduleRuleOwner[moduleRule];
            if (walker.CrashedRules.TryGetValue(moduleRule, out var crashException))
            {
                RecordCrash(crashes, rule.Id, parseResult.SourcePath, crashException);
                continue;
            }

            results.Add((rule, moduleRule));
        }

        return results;
    }
}

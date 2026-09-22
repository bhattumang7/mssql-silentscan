using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Verify.Deployment;

namespace SilentScan.Live.Sweep;

public static class RuleExampleCorpus
{
    public static IReadOnlyList<RuleExampleCase> Build() => Build(RuleDocCatalog.ByRuleId);

    public static IReadOnlyList<RuleExampleCase> Build(IReadOnlyDictionary<string, RuleDocContent> catalog)
    {
        var cases = new List<RuleExampleCase>();

        foreach (var (ruleId, content) in catalog.Where(entry => !StyleRuleFamilies.IsStyleRule(entry.Key)))
        {
            var exampleIndex = 0;
            foreach (var example in content.AllExamples)
            {
                cases.Add(BuildNoncompliantCase(ruleId, exampleIndex, example));

                if (example.CompliantSql is { } compliantSql)
                {
                    cases.Add(BuildCompliantCase(ruleId, exampleIndex, example, compliantSql));
                }

                exampleIndex++;
            }
        }

        return cases;
    }

    private static RuleExampleCase BuildNoncompliantCase(string ruleId, int exampleIndex, RuleDocExample example)
    {
        var noncompliantSql = ModuleBatchNormalizer.Normalize(example.NoncompliantSql);
        return new RuleExampleCase(
            ruleId,
            exampleIndex,
            example.Title,
            RuleExampleVariant.Noncompliant,
            noncompliantSql,
            IsSelfContained: HasObjectDefinition(noncompliantSql),
            BehaviorProof: example.NoncompliantProof,
            RequiresLatestEngine: example.RequiresLatestEngine);
    }

    private static RuleExampleCase BuildCompliantCase(string ruleId, int exampleIndex, RuleDocExample example, string rawCompliantSql)
    {
        var compliantSql = ModuleBatchNormalizer.Normalize(rawCompliantSql);
        if (HasObjectDefinition(compliantSql))
        {
            return new RuleExampleCase(ruleId, exampleIndex, example.Title, RuleExampleVariant.Compliant, compliantSql, IsSelfContained: true, example.CompliantProof, example.RequiresLatestEngine);
        }

        var (prelude, preludeHasObjectDefinition) = ExtractPrelude(
            ModuleBatchNormalizer.Normalize(example.NoncompliantSql),
            skipTriggers: DefinesTrigger(compliantSql));
        var deployable = prelude.Length == 0 ? compliantSql : $"{prelude}\nGO\n{compliantSql}";

        return new RuleExampleCase(
            ruleId,
            exampleIndex,
            example.Title,
            RuleExampleVariant.Compliant,
            deployable,
            IsSelfContained: preludeHasObjectDefinition,
            BehaviorProof: example.CompliantProof,
            RequiresLatestEngine: example.RequiresLatestEngine);
    }

    private static (string Prelude, bool HasObjectDefinition) ExtractPrelude(string noncompliantSql, bool skipTriggers)
    {
        var parseResult = SqlScriptParser.ParseText("corpus-prelude", noncompliantSql);
        if (parseResult.Fragment is not TSqlScript { Batches.Count: > 0 } script)
        {
            return (string.Empty, false);
        }

        var preludeStatements = new List<string>();
        var hasObjectDefinition = false;

        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
        {
            var allowed = DdlStatementWhitelist.IsAllowed(statement, allowProcedureAndTriggerDefinitions: true)
                || MetadataProcedureCalls.IsMetadataCall(statement);
            if (!allowed || (skipTriggers && IsTriggerDefinition(statement)))
            {
                continue;
            }

            preludeStatements.Add(noncompliantSql.Substring(statement.StartOffset, statement.FragmentLength));
            hasObjectDefinition |= DefinesBaseObject(statement);
        }

        return (string.Join("\nGO\n", preludeStatements), hasObjectDefinition);
    }

    private static bool HasObjectDefinition(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("corpus-shape-check", sql);
        if (parseResult.Fragment is not TSqlScript { Batches.Count: > 0 } script)
        {
            return false;
        }

        return script.Batches
            .SelectMany(b => b.Statements)
            .Any(DefinesBaseObject);
    }

    private static bool DefinesTrigger(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("corpus-shape-check", sql);
        return parseResult.Fragment is TSqlScript script
            && script.Batches.SelectMany(b => b.Statements).Any(IsTriggerDefinition);
    }

    private static bool IsTriggerDefinition(TSqlStatement statement) =>
        statement is CreateTriggerStatement or AlterTriggerStatement or CreateOrAlterTriggerStatement;

    private static bool DefinesBaseObject(TSqlStatement statement) => statement switch
    {
        IfStatement ifStatement =>
            (ifStatement.ThenStatement is not null && DefinesBaseObject(ifStatement.ThenStatement))
            || (ifStatement.ElseStatement is not null && DefinesBaseObject(ifStatement.ElseStatement)),
        BeginEndBlockStatement beginEnd => beginEnd.StatementList.Statements.Any(DefinesBaseObject),
        CreateTableStatement or CreateTypeTableStatement or CreateTypeUddtStatement => true,
        CreateViewStatement or AlterViewStatement or CreateOrAlterViewStatement => true,
        CreateFunctionStatement or AlterFunctionStatement or CreateOrAlterFunctionStatement => true,
        CreateProcedureStatement or AlterProcedureStatement or CreateOrAlterProcedureStatement => true,
        CreateSchemaStatement or CreateSynonymStatement or CreateSequenceStatement => true,
        _ => false,
    };
}

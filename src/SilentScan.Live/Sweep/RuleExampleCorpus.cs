using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Verify.Deployment;

namespace SilentScan.Live.Sweep;

public static class RuleExampleCorpus
{
    public static IReadOnlyList<RuleExampleCase> Build() => Build(RuleDocCatalog.ByRuleId);

    public static IReadOnlyList<RuleExampleCase> Build(IReadOnlyDictionary<string, RuleDocContent> catalog)
    {
        var cases = new List<RuleExampleCase>();

        foreach (var (ruleId, content) in catalog)
        {
            var exampleIndex = 0;
            foreach (var example in content.AllExamples)
            {
                cases.Add(BuildNoncompliantCase(ruleId, exampleIndex, example));

                if (example.CompliantSql is { } compliantSql)
                {
                    cases.Add(BuildCompliantCase(ruleId, exampleIndex, example.Title, example.NoncompliantSql, compliantSql));
                }

                exampleIndex++;
            }
        }

        return cases;
    }

    private static RuleExampleCase BuildNoncompliantCase(string ruleId, int exampleIndex, RuleDocExample example) =>
        new(
            ruleId,
            exampleIndex,
            example.Title,
            RuleExampleVariant.Noncompliant,
            example.NoncompliantSql,
            IsSelfContained: HasObjectDefinition(example.NoncompliantSql));

    private static RuleExampleCase BuildCompliantCase(string ruleId, int exampleIndex, string title, string noncompliantSql, string compliantSql)
    {
        if (HasObjectDefinition(compliantSql))
        {
            return new RuleExampleCase(ruleId, exampleIndex, title, RuleExampleVariant.Compliant, compliantSql, IsSelfContained: true);
        }

        var (prelude, preludeHasObjectDefinition) = ExtractPrelude(noncompliantSql);
        var deployable = prelude.Length == 0 ? compliantSql : $"{prelude}\n{compliantSql}";

        return new RuleExampleCase(
            ruleId,
            exampleIndex,
            title,
            RuleExampleVariant.Compliant,
            deployable,
            IsSelfContained: preludeHasObjectDefinition);
    }

    private static (string Prelude, bool HasObjectDefinition) ExtractPrelude(string noncompliantSql)
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
            if (!DdlStatementWhitelist.IsAllowed(statement, allowProcedureAndTriggerDefinitions: true))
            {
                continue;
            }

            preludeStatements.Add(noncompliantSql.Substring(statement.StartOffset, statement.FragmentLength));
            hasObjectDefinition |= DefinesBaseObject(statement);
        }

        return (string.Join('\n', preludeStatements), hasObjectDefinition);
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

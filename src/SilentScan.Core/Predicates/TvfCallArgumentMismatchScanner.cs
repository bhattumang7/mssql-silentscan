using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public static class TvfCallArgumentMismatchScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<TvfCallArgumentMismatchFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<TvfCallArgumentMismatchFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : VariableTrackingModuleRule(catalog), IModuleRule
    {
        public List<TvfCallArgumentMismatchFinding> Findings { get; } = [];

        public void OnEnterFromClause(FromClause node, ModuleWalker walker)
        {
            foreach (var tableReference in node.TableReferences)
            {
                Flatten(tableReference, walker);
            }
        }

        private void Flatten(TableReference tableReference, ModuleWalker walker)
        {
            switch (tableReference)
            {
                case JoinTableReference join:
                    Flatten(join.FirstTableReference, walker);
                    Flatten(join.SecondTableReference, walker);
                    break;

                case JoinParenthesisTableReference parenthesis:
                    Flatten(parenthesis.Join, walker);
                    break;

                case SchemaObjectFunctionTableReference function:
                    VisitFunctionReference(function, walker);
                    break;
            }
        }

        private void VisitFunctionReference(SchemaObjectFunctionTableReference function, ModuleWalker walker)
        {
            var qualifiedName = Catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(function.SchemaObject));
            if (!Catalog.TryGetTableValuedFunctionKind(qualifiedName, out var kind) || kind != TableValuedFunctionKind.Inline
                || !Catalog.TryGetProcedureParameters(qualifiedName, out var formalParameters))
            {
                return;
            }

            var count = Math.Min(function.Parameters.Count, formalParameters.Count);
            for (var i = 0; i < count; i++)
            {
                var formal = formalParameters[i];
                if (formal.Type is not { } formalType)
                {
                    continue;
                }

                var argumentExpression = function.Parameters[i];
                var argumentType = ScalarExpressionResolver.ResolveScalarType(
                    argumentExpression, [], sourcePath,
                    new ScalarExpressionResolver.ScalarTypeContext(null, Catalog.TypeAliases, Catalog, VariableTypes));

                if (WriteLossClassifier.Classify(formalType, argumentType, argumentExpression, isVariableTarget: true) is not { } kindResult)
                {
                    continue;
                }

                var display = argumentExpression is VariableReference variableRef
                    ? variableRef.Name
                    : FragmentTextRenderer.Render(argumentExpression);

                Findings.Add(new TvfCallArgumentMismatchFinding(
                    walker.CurrentProcScope, qualifiedName, formal.Name, display, argumentType!.ToString(), formalType.ToString(),
                    kindResult, sourcePath, argumentExpression.StartLine, argumentExpression.StartColumn));
            }
        }
    }
}

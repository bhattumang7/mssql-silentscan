using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class TruncateSwallowedScanner
{
    public static IReadOnlyList<TruncateSwallowedFinding> Scan(SqlParseResult parseResult)
    {
        var rule = CreateRule(parseResult.SourcePath);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath) => new(sourcePath);

    internal static IReadOnlyList<TruncateSwallowedFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath) : IModuleRule
    {
        public List<TruncateSwallowedFinding> Findings { get; } = [];

        public void OnEnterTryCatchStatement(TryCatchStatement node, ModuleWalker walker)
        {
            if (!ContainsTruncate(node.TryStatements))
            {
                return;
            }

            if (!ContainsPropagatingStatement(node.CatchStatements))
            {
                foreach (var truncate in FindTruncates(node.TryStatements))
                {
                    Findings.Add(new TruncateSwallowedFinding(sourcePath, truncate.StartLine, truncate.StartColumn));
                }
            }
        }

        private static bool ContainsTruncate(TSqlFragment? fragment) => FindTruncates(fragment).Count != 0;

        private static List<TruncateTableStatement> FindTruncates(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return [];
            }

            var collector = new TruncateVisibilityCollector();
            fragment.Accept(collector);
            return collector.Nodes;
        }

        private static bool ContainsPropagatingStatement(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return false;
            }

            var throwCollector = new NodeCollector<ThrowStatement>();
            fragment.Accept(throwCollector);
            if (throwCollector.Nodes.Count != 0)
            {
                return true;
            }

            var raiseErrorCollector = new NodeCollector<RaiseErrorStatement>();
            fragment.Accept(raiseErrorCollector);
            if (raiseErrorCollector.Nodes.Any(RaisesToTheCaller))
            {
                return true;
            }

            var executeCollector = new NodeCollector<ExecuteStatement>();
            fragment.Accept(executeCollector);
            return executeCollector.Nodes.Any(MayRaiseAnError);
        }

    private static bool RaisesToTheCaller(RaiseErrorStatement raise) =>
        raise.SecondParameter is not IntegerLiteral { Value: var severity } || !int.TryParse(severity, out var level) || level > 10;

        private static bool MayRaiseAnError(ExecuteStatement execute)
        {
            if (execute.ExecuteSpecification.ExecutableEntity is not ExecutableProcedureReference
                {
                    ProcedureReference.ProcedureReference.Name: { BaseIdentifier.Value: var name } procedureName,
                })
            {
                return true;
            }

            var isSystemProcedure = procedureName.SchemaIdentifier is null
                && (name.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("xp_", StringComparison.OrdinalIgnoreCase));
            return !isSystemProcedure || name.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class NodeCollector<T> : TSqlFragmentVisitor
            where T : TSqlFragment
        {
            public List<T> Nodes { get; } = [];

            public override void Visit(TSqlFragment fragment)
            {
                if (fragment is T match)
                {
                    Nodes.Add(match);
                }

                base.Visit(fragment);
            }
        }

        private sealed class TruncateVisibilityCollector : TSqlFragmentVisitor
        {
            public List<TruncateTableStatement> Nodes { get; } = [];

            public override void Visit(TSqlFragment fragment)
            {
                if (fragment is TruncateTableStatement { TableName.BaseIdentifier.Value: { } name } match && !name.StartsWith('#'))
                {
                    Nodes.Add(match);
                }
            }

            public override void ExplicitVisit(TryCatchStatement node)
            {
                node.CatchStatements?.Accept(this);

                if (ContainsPropagatingStatement(node.CatchStatements))
                {
                    node.TryStatements?.Accept(this);
                }
            }
        }
    }
}

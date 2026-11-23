using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class MultiReferencedCteScanner
{
    public static IReadOnlyList<MultiReferencedCteFinding> Scan(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog ?? new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog? catalog = null) => new(sourcePath, catalog?.IdentifierComparer ?? StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<MultiReferencedCteFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, StringComparer identifierComparer) : IModuleRule
    {
        public List<MultiReferencedCteFinding> Findings { get; } = [];

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            InspectWithClause(node.WithCtesAndXmlNamespaces, node.QueryExpression);
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker) =>
            InspectWithClause(node.WithCtesAndXmlNamespaces, node.InsertSpecification);

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectWithClause(node.WithCtesAndXmlNamespaces, node.UpdateSpecification);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectWithClause(node.WithCtesAndXmlNamespaces, node.DeleteSpecification);

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectWithClause(node.WithCtesAndXmlNamespaces, node.MergeSpecification);

        private void InspectWithClause(WithCtesAndXmlNamespaces? withClause, TSqlFragment mainBody)
        {
            if (withClause is null)
            {
                return;
            }

            var ctes = withClause.CommonTableExpressions;
            var reachable = ReachableCtes(ctes, mainBody);

            foreach (var cte in ctes)
            {
                var name = cte.ExpressionName.Value;

                var referenceLines = CollectReferences(mainBody, name);
                foreach (var other in ctes.Where(other => !ReferenceEquals(other, cte) && reachable.Contains(other)))
                {
                    referenceLines.AddRange(CollectReferences(other.QueryExpression, name));
                }

                if (referenceLines.Count >= 2 && !IsPlainSingleTableRead(cte, ctes))
                {
                    Findings.Add(new MultiReferencedCteFinding(
                        name, referenceLines.Count, [.. referenceLines.OrderBy(l => l)],
                        sourcePath, cte.StartLine));
                }
            }
        }

        private HashSet<CommonTableExpression> ReachableCtes(IList<CommonTableExpression> ctes, TSqlFragment mainBody)
        {
            var reachable = new HashSet<CommonTableExpression>();
            var pending = new Queue<TSqlFragment>();
            pending.Enqueue(mainBody);
            while (pending.Count > 0)
            {
                var fragment = pending.Dequeue();
                foreach (var cte in ctes.Where(cte => !reachable.Contains(cte) && CollectReferences(fragment, cte.ExpressionName.Value).Count > 0))
                {
                    reachable.Add(cte);
                    pending.Enqueue(cte.QueryExpression);
                }
            }

            return reachable;
        }

        private bool IsPlainSingleTableRead(CommonTableExpression cte, IList<CommonTableExpression> ctes)
        {
            if (cte.QueryExpression is not QuerySpecification
                {
                    TopRowFilter: null,
                    GroupByClause: null,
                    HavingClause: null,
                    UniqueRowFilter: UniqueRowFilter.NotSpecified,
                    FromClause.TableReferences: [NamedTableReference { SchemaObject: var source }],
                } specification
                || cte.QueryExpression.OrderByClause is not null
                || (source.SchemaIdentifier is null && ctes.Any(c => identifierComparer.Equals(c.ExpressionName.Value, source.BaseIdentifier.Value))))
            {
                return false;
            }

            var inspector = new CostlyConstructFinder();
            specification.Accept(inspector);
            return !inspector.Found;
        }

        private sealed class CostlyConstructFinder : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(FunctionCall node) => Found = true;

            public override void ExplicitVisit(ScalarSubquery node) => Found = true;

            public override void ExplicitVisit(ExistsPredicate node) => Found = true;

            public override void ExplicitVisit(InPredicate node)
            {
                if (node.Subquery is not null)
                {
                    Found = true;
                }

                base.ExplicitVisit(node);
            }
        }

        private List<int> CollectReferences(TSqlFragment fragment, string cteName)
        {
            var collector = new ReferenceCollector(cteName, identifierComparer);
            fragment.Accept(collector);
            return collector.Lines;
        }

        private sealed class ReferenceCollector(string cteName, StringComparer identifierComparer) : TSqlFragmentVisitor
        {
            public List<int> Lines { get; } = [];

            public override void ExplicitVisit(NamedTableReference node)
            {
                if (node.SchemaObject.SchemaIdentifier is null && identifierComparer.Equals(node.SchemaObject.BaseIdentifier.Value, cteName))
                {
                    Lines.Add(node.StartLine);
                }

                base.ExplicitVisit(node);
            }
        }
    }
}

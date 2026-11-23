using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static partial class UnindexedTempTableUsageScanner
{
    public static IReadOnlyList<UnindexedTempTableUsageFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(parseResult, catalog, rule);
    }

    internal static Rule CreateRule(DatabaseCatalog catalog) => new(catalog);

    public static Dictionary<string, List<Declaration>> CollectDeclarationsByScope(
        IReadOnlyList<SqlParseResult> parseResults, DatabaseCatalog catalog)
    {
        var byScope = new Dictionary<string, List<Declaration>>(StringComparer.Ordinal);

        foreach (var parseResult in parseResults)
        {
            var rule = CreateRule(catalog);
            var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
            parseResult.Fragment.Accept(walker);

            foreach (var declaration in rule.Declarations)
            {
                if (declaration.Scope is not { } scope)
                {
                    continue;
                }

                if (!byScope.TryGetValue(scope, out var list))
                {
                    list = [];
                    byScope[scope] = list;
                }

                list.Add(declaration);
            }
        }

        return byScope;
    }

    internal static IReadOnlyList<UnindexedTempTableUsageFinding> HarvestCrossBoundary(
        SqlParseResult parseResult, DatabaseCatalog catalog, Rule rule,
        IReadOnlyDictionary<string, List<Declaration>> outerDeclarationsByScope)
    {
        var tempIdentifierComparer = TypeInference.Collation.IdentifierComparer(catalog.EffectiveTempdbCollation);
        var findings = new List<UnindexedTempTableUsageFinding>();

        foreach (var usage in rule.Usages)
        {
            if (usage.Scope is not { } scope)
            {
                continue;
            }

            var shadowedByInBatchDeclaration = rule.Declarations.Any(d =>
                d.Scope == scope && tempIdentifierComparer.Equals(d.TempTableName, usage.TempTableName));

            if (shadowedByInBatchDeclaration || !outerDeclarationsByScope.TryGetValue(scope, out var declarations))
            {
                continue;
            }

            var declaration = declarations.FirstOrDefault(d => tempIdentifierComparer.Equals(d.TempTableName, usage.TempTableName));
            if (declaration is null)
            {
                continue;
            }

            AddFindingIfUnindexed(findings, parseResult, catalog, declaration, usage, rule);
        }

        return OrderFindings(findings);
    }

    private static void AddFindingIfUnindexed(
        List<UnindexedTempTableUsageFinding> findings, SqlParseResult parseResult, DatabaseCatalog catalog,
        Declaration declaration, Usage usage, Rule rule)
    {
        var temp = catalog.Find(declaration.TempQualifiedName, declaration.Scope);
        if (temp is null || temp.Indexes.Count != 0 || rule.DynamicSqlScopes.Contains(declaration.Scope))
        {
            return;
        }

        findings.Add(new UnindexedTempTableUsageFinding(
            usage.Kind,
            declaration.TempQualifiedName,
            parseResult.SourcePath,
            declaration.Line,
            usage.Line,
            usage.Column));
    }

    private static IReadOnlyList<UnindexedTempTableUsageFinding> OrderFindings(List<UnindexedTempTableUsageFinding> findings)
    {
        return
        [
            .. findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.DeclarationLine),
        ];
    }

    internal static IReadOnlyList<UnindexedTempTableUsageFinding> Harvest(SqlParseResult parseResult, DatabaseCatalog catalog, Rule rule)
    {
        var tempIdentifierComparer = TypeInference.Collation.IdentifierComparer(catalog.EffectiveTempdbCollation);
        var findings = new List<UnindexedTempTableUsageFinding>();

        foreach (var declaration in rule.Declarations)
        {
            var usage = rule.Usages.FirstOrDefault(u =>
                u.Scope == declaration.Scope
                && tempIdentifierComparer.Equals(u.TempTableName, declaration.TempTableName));

            if (usage is null)
            {
                continue;
            }

            AddFindingIfUnindexed(findings, parseResult, catalog, declaration, usage, rule);
        }

        return OrderFindings(findings);
    }

    public sealed record Declaration(string TempTableName, string TempQualifiedName, string? Scope, int Line);

    internal sealed record Usage(string TempTableName, string? Scope, UnindexedTempTableUsageKind Kind, int Line, int Column);

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed partial class Rule(DatabaseCatalog catalog) : IModuleRule
    {
        public List<Declaration> Declarations { get; } = [];

        public List<Usage> Usages { get; } = [];

        public HashSet<string?> DynamicSqlScopes { get; } = [];

        public void OnEnterExecuteStatement(ExecuteStatement node, ModuleWalker walker)
        {
            if (MayCreateAnIndex(node))
            {
                DynamicSqlScopes.Add(walker.CurrentProcScope);
            }
        }

        private static bool MayCreateAnIndex(ExecuteStatement node)
        {
            var texts = new List<ScalarExpression>();
            switch (node.ExecuteSpecification.ExecutableEntity)
            {
                case ExecutableStringList list:
                    texts.AddRange(list.Strings);
                    break;
                case ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var procedure } reference
                    when string.Equals(procedure, "sp_executesql", StringComparison.OrdinalIgnoreCase):
                    if (reference.Parameters.Count > 0)
                    {
                        texts.Add(reference.Parameters[0].ParameterValue);
                    }

                    break;
                default:
                    return false;
            }

            return texts.Any(text => text is not StringLiteral literal
                || IndexKeywordRegex().IsMatch(literal.Value));
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\b(INDEX|PRIMARY\s+KEY|UNIQUE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
        private static partial System.Text.RegularExpressions.Regex IndexKeywordRegex();

        public void OnEnterSelectStatementScope(SelectStatement node, ModuleWalker walker)
        {
            if (node.Into is { BaseIdentifier.Value: var tempName } into && tempName.StartsWith('#'))
            {
                var qualified = catalog.Find(SchemaObjectNameHelper.Qualify(into), walker.CurrentProcScope)?.QualifiedName
                    ?? SchemaObjectNameHelper.Qualify(into);
                Declarations.Add(new Declaration(tempName, qualified, walker.CurrentProcScope, node.StartLine));
            }
        }

        public void OnEnterJoinSearchCondition(QualifiedJoin node, ModuleWalker walker)
        {
            TryRecordJoinOperand(node.FirstTableReference, node, walker);
            TryRecordJoinOperand(node.SecondTableReference, node, walker);
        }

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var tableReferences = node.FromClause?.TableReferences;
            if (tableReferences is null)
            {
                return;
            }

            RecordSoloFilteredTable(node.WhereClause, tableReferences, walker);

            if (tableReferences.Count >= 2)
            {
                foreach (var reference in tableReferences)
                {
                    TryRecordJoinOperand(reference, reference, walker);
                }
            }

            foreach (var reference in tableReferences)
            {
                RecordUnqualifiedCrossJoins(reference, node.WhereClause, walker);
            }
        }

        private void RecordSoloFilteredTable(WhereClause? whereClause, IList<TableReference> tableReferences, ModuleWalker walker)
        {
            if (whereClause is { } where
                && tableReferences is [NamedTableReference { SchemaObject.BaseIdentifier.Value: var soloName }]
                && soloName.StartsWith('#'))
            {
                Usages.Add(new Usage(soloName, walker.CurrentProcScope, UnindexedTempTableUsageKind.FilteredInWhere, where.StartLine, where.StartColumn));
            }
        }

        private void RecordUnqualifiedCrossJoins(TableReference reference, WhereClause? whereClause, ModuleWalker walker)
        {
            foreach (var unqualified in PredicateTreeWalker.FlattenUnqualifiedJoins(reference))
            {
                if (unqualified.UnqualifiedJoinType != UnqualifiedJoinType.CrossJoin)
                {
                    continue;
                }

                if (HasCorrelatingWherePredicate(whereClause, unqualified.FirstTableReference, unqualified.SecondTableReference))
                {
                    TryRecordJoinOperand(unqualified.FirstTableReference, unqualified, walker);
                    TryRecordJoinOperand(unqualified.SecondTableReference, unqualified, walker);
                }
                else
                {
                    TryRecordFilteredInWhere(unqualified.FirstTableReference, whereClause, walker);
                    TryRecordFilteredInWhere(unqualified.SecondTableReference, whereClause, walker);
                }
            }
        }

        private static bool HasCorrelatingWherePredicate(WhereClause? whereClause, TableReference first, TableReference second)
        {
            if (whereClause is null || GetReferenceKey(first) is not { } firstKey || GetReferenceKey(second) is not { } secondKey)
            {
                return false;
            }

            foreach (var predicate in PredicateTreeWalker.FlattenAnd(whereClause.SearchCondition))
            {
                if (predicate is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } cmp)
                {
                    continue;
                }

                var leftKey = GetColumnQualifier(cmp.FirstExpression);
                var rightKey = GetColumnQualifier(cmp.SecondExpression);

                if ((MatchesKey(leftKey, firstKey) && MatchesKey(rightKey, secondKey))
                    || (MatchesKey(leftKey, secondKey) && MatchesKey(rightKey, firstKey)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetReferenceKey(TableReference reference) => reference switch
        {
            NamedTableReference { Alias.Value: var alias } => alias,
            NamedTableReference { SchemaObject.BaseIdentifier.Value: var name } => name,
            _ => null,
        };

        private static string? GetColumnQualifier(ScalarExpression expression) =>
            expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., var qualifier, _] }
                ? qualifier.Value
                : null;

        private static bool MatchesKey(string? candidate, string key) =>
            candidate is not null && string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase);

        private void TryRecordJoinOperand(TableReference side, TSqlFragment joinNode, ModuleWalker walker)
        {
            if (side is NamedTableReference { SchemaObject.BaseIdentifier.Value: var name } && name.StartsWith('#'))
            {
                Usages.Add(new Usage(name, walker.CurrentProcScope, UnindexedTempTableUsageKind.JoinOperand, joinNode.StartLine, joinNode.StartColumn));
            }
        }

        private void TryRecordFilteredInWhere(TableReference side, WhereClause? whereClause, ModuleWalker walker)
        {
            if (side is NamedTableReference { SchemaObject.BaseIdentifier.Value: var name } && name.StartsWith('#')
                && HasOwnFilteringWherePredicate(whereClause, side))
            {
                Usages.Add(new Usage(name, walker.CurrentProcScope, UnindexedTempTableUsageKind.FilteredInWhere, whereClause!.StartLine, whereClause.StartColumn));
            }
        }

        private static bool HasOwnFilteringWherePredicate(WhereClause? whereClause, TableReference side)
        {
            if (whereClause is null || GetReferenceKey(side) is not { } key)
            {
                return false;
            }

            foreach (var predicate in PredicateTreeWalker.FlattenAnd(whereClause.SearchCondition))
            {
                if (predicate is not BooleanComparisonExpression cmp)
                {
                    continue;
                }

                if (MatchesOwnSide(cmp.FirstExpression, key) || MatchesOwnSide(cmp.SecondExpression, key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesOwnSide(ScalarExpression expression, string key) =>
            expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers }
                && (identifiers.Count == 1 || MatchesKey(identifiers[^2].Value, key));
    }
}

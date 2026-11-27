using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ModuleReachableObjectWalker
{
    public readonly record struct Touch(
        string ObjectQualifiedName, string? IndexName, bool IsIndexedView, IReadOnlyList<int> Offsets,
        bool IsComputedColumnIndex = false);

    public static bool TryFindTouch(TSqlFragment moduleBody, DatabaseCatalog catalog, out Touch touch)
    {
        var collector = new StatementCollector(catalog);
        moduleBody.Accept(collector);

        foreach (var (rawName, _) in collector.QualifiedNames)
        {
            if (TryInspectQualifiedName(catalog.ResolveSynonymName(rawName), catalog, collector, out touch))
            {
                return true;
            }
        }

        touch = default;
        return false;
    }

    private static bool TryInspectQualifiedName(string qualifiedName, DatabaseCatalog catalog, StatementCollector collector, out Touch touch)
    {
        if (catalog.IsIndexedView(qualifiedName))
        {
            touch = new Touch(
                qualifiedName, IndexName: null, IsIndexedView: true,
                [.. collector.QualifiedNames.Where(n => catalog.ResolveSynonymName(n.Name) == qualifiedName).Select(n => n.Offset)]);
            return true;
        }

        if (catalog.Find(qualifiedName) is { } table)
        {
            var restated = table.Indexes.FirstOrDefault(i =>
                i.IsFiltered && i.FilterDefinition is not null
                && ModuleRestatesFilter(i.FilterDefinition, i.Name, table.QualifiedName, catalog, collector.Statements).Count > 0);
            if (restated is not null)
            {
                touch = new Touch(
                    qualifiedName, restated.Name, IsIndexedView: false,
                    ModuleRestatesFilter(restated.FilterDefinition!, restated.Name, table.QualifiedName, catalog, collector.Statements));
                return true;
            }

            foreach (var index in table.Indexes.Where(i => !i.IsDisabled && !i.IsColumnstore && !i.IsFiltered && i.KeyColumns.Count > 0))
            {
                var leading = index.KeyColumns[0];
                if (table.FindColumn(leading, catalog.IdentifierComparer) is not { IsComputed: true })
                {
                    continue;
                }

                var offsets = StatementsComparing(leading, table.QualifiedName, catalog, collector.Statements);
                if (offsets.Count > 0)
                {
                    touch = new Touch(qualifiedName, index.Name, IsIndexedView: false, offsets, IsComputedColumnIndex: true);
                    return true;
                }
            }
        }

        touch = default;
        return false;
    }

    private static List<int> StatementsComparing(
        string columnName, string tableQualifiedName, DatabaseCatalog catalog, IReadOnlyList<StatementPredicates> statements) =>
        [.. statements
            .Where(statement => statement.IsQuery && statement.Compared.Any(compared =>
                catalog.IdentifierComparer.Equals(compared.Column, columnName)
                && statement.Tables.Any(table => catalog.IdentifierComparer.Equals(table.QualifiedName, tableQualifiedName)
                    && (compared.Qualifier is null
                        ? statement.Tables.Count == 1
                        : catalog.IdentifierComparer.Equals(table.AliasKey, compared.Qualifier)))))
            .Select(statement => statement.Offset)];

    private static IEnumerable<ComparedColumn> ComparedColumns(BooleanExpression conjunct)
    {
        IEnumerable<ScalarExpression> operands = conjunct switch
        {
            BooleanComparisonExpression
            {
                ComparisonType: BooleanComparisonType.Equals or BooleanComparisonType.LessThan or BooleanComparisonType.GreaterThan
                    or BooleanComparisonType.LessThanOrEqualTo or BooleanComparisonType.GreaterThanOrEqualTo,
            } comparison => [comparison.FirstExpression, comparison.SecondExpression],
            BooleanTernaryExpression { TernaryExpressionType: BooleanTernaryExpressionType.Between } between => [between.FirstExpression],
            InPredicate { NotDefined: false } membership => [membership.Expression],
            _ => [],
        };

        return operands.OfType<ColumnReferenceExpression>()
            .Where(column => column.MultiPartIdentifier is not null)
            .Select(column => column.MultiPartIdentifier.Identifiers)
            .Select(parts => new ComparedColumn(parts.Count > 1 ? parts[^2].Value : null, parts[^1].Value));
    }

    private static List<int> ModuleRestatesFilter(
        string filterDefinition, string? indexName, string tableQualifiedName, DatabaseCatalog catalog,
        IReadOnlyList<StatementPredicates> statements)
    {
        var parsed = SqlScriptParser.ParseText("filter-definition.sql", $"SELECT 1 WHERE {filterDefinition};", initialQuotedIdentifiers: true, catalog.CompatibilityLevel);
        if (parsed.HasErrors
            || parsed.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { WhereClause.SearchCondition: { } filter } }] }] })
        {
            return [];
        }

        var required = PredicateTreeWalker.FlattenAnd(filter).Select(Normalize).ToList();
        if (required.Count == 0 || required.Any(r => r.Qualifiers.Count > 0))
        {
            return [];
        }

        var offsets = new List<int>();
        foreach (var statement in statements)
        {
            var aliases = statement.Tables
                .Where(t => catalog.IdentifierComparer.Equals(t.QualifiedName, tableQualifiedName)
                    && !t.HintedIndexes.Contains(indexName ?? string.Empty, catalog.IdentifierComparer))
                .Select(t => t.AliasKey)
                .ToList();
            if (aliases.Count == 0)
            {
                continue;
            }

            var satisfied = required.All(need => statement.Conjuncts.Any(have =>
                have.Text == need.Text
                && have.Qualifiers.All(q => aliases.Contains(q, catalog.IdentifierComparer))));
            if (satisfied)
            {
                offsets.Add(statement.Offset);
            }
        }

        return offsets;
    }

    private static NormalizedPredicate Normalize(BooleanExpression expression)
    {
        var tokens = expression.ScriptTokenStream;
        var significant = new List<TSqlParserToken>();
        for (var i = expression.FirstTokenIndex; i <= expression.LastTokenIndex; i++)
        {
            if (tokens[i].TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
                or TSqlTokenType.LeftParenthesis or TSqlTokenType.RightParenthesis))
            {
                significant.Add(tokens[i]);
            }
        }

        var text = new System.Text.StringBuilder();
        var qualifiers = new List<string>();
        var position = 0;
        while (position < significant.Count)
        {
            var token = significant[position];
            var isName = token.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier;
            if (isName && position + 1 < significant.Count && significant[position + 1].TokenType == TSqlTokenType.Dot)
            {
                qualifiers.Add(Unquote(token.Text));
                position += 2;
                continue;
            }

            text.Append(isName ? Unquote(token.Text) : token.Text).Append(' ');
            position++;
        }

        return new NormalizedPredicate(text.ToString().ToUpperInvariant(), qualifiers);
    }

    private static string Unquote(string identifier) =>
        identifier.Length >= 2 && ((identifier[0] == '[' && identifier[^1] == ']') || (identifier[0] == '"' && identifier[^1] == '"'))
            ? identifier[1..^1]
            : identifier;

    private readonly record struct NormalizedPredicate(string Text, List<string> Qualifiers);

    private readonly record struct StatementTable(string QualifiedName, string AliasKey, IReadOnlyList<string> HintedIndexes);

    private readonly record struct ComparedColumn(string? Qualifier, string Column);

    private sealed record StatementPredicates(
        List<StatementTable> Tables, List<NormalizedPredicate> Conjuncts, int Offset, List<ComparedColumn> Compared, bool IsQuery);

    private sealed class StatementCollector(DatabaseCatalog catalog) : TSqlFragmentVisitor
    {
        public List<(string Name, int Offset)> QualifiedNames { get; } = [];

        public List<StatementPredicates> Statements { get; } = [];

        private readonly HashSet<TableReference> _writeTargets = [];

        public override void Visit(NamedTableReference node)
        {
            if (!_writeTargets.Contains(node) && node.TableHints.All(hint => hint.HintKind != TableHintKind.NoExpand))
            {
                QualifiedNames.Add((SchemaObjectNameHelper.Qualify(node.SchemaObject), node.StartOffset));
            }
        }

        public override void Visit(InsertSpecification node) => ExcludeWriteTarget(node.Target, fromClause: null);

        public override void Visit(MergeSpecification node) => ExcludeWriteTarget(node.Target, fromClause: null);

        public override void Visit(QuerySpecification node) =>
            Record(node.FromClause, node.WhereClause, node.StartOffset, isQuery: true);

        public override void Visit(UpdateSpecification node)
        {
            ExcludeWriteTarget(node.Target, node.FromClause);
            Record(node.FromClause, node.WhereClause, node.StartOffset);
        }

        public override void Visit(DeleteSpecification node)
        {
            ExcludeWriteTarget(node.Target, node.FromClause);
            Record(node.FromClause, node.WhereClause, node.StartOffset);
        }

        private void ExcludeWriteTarget(TableReference target, FromClause? fromClause)
        {
            _writeTargets.Add(target);
            if (target is not NamedTableReference named || fromClause is null)
            {
                return;
            }

            var name = named.SchemaObject.BaseIdentifier.Value;
            var qualified = SchemaObjectNameHelper.Qualify(named.SchemaObject);
            foreach (var source in fromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenNamedTables))
            {
                var isTarget = source.Alias is { Value: var alias }
                    ? named.SchemaObject.SchemaIdentifier is null && catalog.IdentifierComparer.Equals(alias, name)
                    : catalog.IdentifierComparer.Equals(SchemaObjectNameHelper.Qualify(source.SchemaObject), qualified);
                if (isTarget)
                {
                    _writeTargets.Add(source);
                }
            }
        }

        private void Record(FromClause? fromClause, WhereClause? whereClause, int offset, bool isQuery = false)
        {
            if (whereClause?.SearchCondition is not { } condition)
            {
                return;
            }

            var tables = new List<StatementTable>();
            foreach (var named in (fromClause?.TableReferences ?? [])
                .SelectMany(PredicateTreeWalker.FlattenNamedTables)
                .Where(named => !_writeTargets.Contains(named)))
            {
                tables.Add(new StatementTable(
                    catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject)),
                    named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value,
                    [.. named.TableHints.OfType<IndexTableHint>().SelectMany(hint => hint.IndexValues)
                        .Select(value => value.Identifier?.Value).OfType<string>()]));
            }

            var conjuncts = PredicateTreeWalker.FlattenAnd(condition).ToList();
            Statements.Add(new StatementPredicates(
                tables, [.. conjuncts.Select(Normalize)], offset, [.. conjuncts.SelectMany(ComparedColumns)], isQuery));
        }
    }
}

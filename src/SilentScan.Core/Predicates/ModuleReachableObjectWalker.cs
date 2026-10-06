using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ModuleReachableObjectWalker
{
    public readonly record struct Touch(string ObjectQualifiedName, string? IndexName, bool IsIndexedView);

    public static bool TryFindTouch(TSqlFragment moduleBody, DatabaseCatalog catalog, out Touch touch)
    {
        var collector = new StatementCollector(catalog);
        moduleBody.Accept(collector);

        foreach (var rawName in collector.QualifiedNames)
        {
            if (TryInspectQualifiedName(catalog.ResolveSynonymName(rawName), catalog, collector.Statements, out touch))
            {
                return true;
            }
        }

        touch = default;
        return false;
    }

    private static bool TryInspectQualifiedName(string qualifiedName, DatabaseCatalog catalog, IReadOnlyList<StatementPredicates> statements, out Touch touch)
    {
        if (catalog.IsIndexedView(qualifiedName))
        {
            touch = new Touch(qualifiedName, IndexName: null, IsIndexedView: true);
            return true;
        }

        if (catalog.Find(qualifiedName) is { } table)
        {
            foreach (var index in table.Indexes.Where(i => i.IsFiltered && i.FilterDefinition is not null))
            {
                if (ModuleRestatesFilter(index.FilterDefinition!, table.QualifiedName, catalog, statements))
                {
                    touch = new Touch(qualifiedName, index.Name, IsIndexedView: false);
                    return true;
                }
            }
        }

        touch = default;
        return false;
    }

    private static bool ModuleRestatesFilter(string filterDefinition, string tableQualifiedName, DatabaseCatalog catalog, IReadOnlyList<StatementPredicates> statements)
    {
        var parsed = SqlScriptParser.ParseText("filter-definition.sql", $"SELECT 1 WHERE {filterDefinition};", initialQuotedIdentifiers: true, catalog.CompatibilityLevel);
        if (parsed.HasErrors
            || parsed.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { WhereClause.SearchCondition: { } filter } }] }] })
        {
            return false;
        }

        var required = PredicateTreeWalker.FlattenAnd(filter).Select(Normalize).ToList();
        if (required.Count == 0 || required.Any(r => r.Qualifiers.Count > 0))
        {
            return false;
        }

        foreach (var statement in statements)
        {
            var aliases = statement.Tables
                .Where(t => catalog.IdentifierComparer.Equals(t.QualifiedName, tableQualifiedName))
                .Select(t => t.AliasKey)
                .ToList();
            if (aliases.Count == 0)
            {
                continue;
            }

            var satisfied = required.All(need => statement.Conjuncts.Any(have =>
                have.Text == need.Text
                && have.Qualifiers.All(q => aliases.Any(a => catalog.IdentifierComparer.Equals(a, q)))));
            if (satisfied)
            {
                return true;
            }
        }

        return false;
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
        for (var i = 0; i < significant.Count; i++)
        {
            var token = significant[i];
            var isName = token.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier;
            if (isName && i + 1 < significant.Count && significant[i + 1].TokenType == TSqlTokenType.Dot)
            {
                qualifiers.Add(Unquote(token.Text));
                i++;
                continue;
            }

            text.Append(isName ? Unquote(token.Text) : token.Text).Append(' ');
        }

        return new NormalizedPredicate(text.ToString().ToUpperInvariant(), qualifiers);
    }

    private static string Unquote(string identifier) =>
        identifier.Length >= 2 && ((identifier[0] == '[' && identifier[^1] == ']') || (identifier[0] == '"' && identifier[^1] == '"'))
            ? identifier[1..^1]
            : identifier;

    private readonly record struct NormalizedPredicate(string Text, List<string> Qualifiers);

    private readonly record struct StatementTable(string QualifiedName, string AliasKey);

    private sealed record StatementPredicates(List<StatementTable> Tables, List<NormalizedPredicate> Conjuncts);

    private sealed class StatementCollector(DatabaseCatalog catalog) : TSqlFragmentVisitor
    {
        public List<string> QualifiedNames { get; } = [];

        public List<StatementPredicates> Statements { get; } = [];

        public override void Visit(NamedTableReference node) =>
            QualifiedNames.Add(SchemaObjectNameHelper.Qualify(node.SchemaObject));

        public override void Visit(QuerySpecification node) =>
            Record(node.FromClause, node.WhereClause, target: null);

        public override void Visit(UpdateSpecification node) =>
            Record(node.FromClause, node.WhereClause, node.Target);

        public override void Visit(DeleteSpecification node) =>
            Record(node.FromClause, node.WhereClause, node.Target);

        private void Record(FromClause? fromClause, WhereClause? whereClause, TableReference? target)
        {
            if (whereClause?.SearchCondition is not { } condition)
            {
                return;
            }

            var tables = new List<StatementTable>();
            var references = (fromClause?.TableReferences ?? []).Concat(target is null ? [] : [target]);
            foreach (var reference in references)
            {
                foreach (var named in PredicateTreeWalker.FlattenNamedTables(reference))
                {
                    tables.Add(new StatementTable(
                        catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(named.SchemaObject)),
                        named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value));
                }
            }

            Statements.Add(new StatementPredicates(tables, [.. PredicateTreeWalker.FlattenAnd(condition).Select(Normalize)]));
        }
    }
}

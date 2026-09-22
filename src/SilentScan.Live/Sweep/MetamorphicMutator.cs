using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Live.Sweep;

public static class MetamorphicMutator
{
    public sealed record Mutation(string Name, string MutatedSql);

    private readonly record struct Edit(int Start, int Length, string Replacement);

    public static IReadOnlyList<Mutation> Mutate(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("metamorphic-mutator", sql);
        if (parseResult.HasErrors)
        {
            return [];
        }

        var tokens = parseResult.Fragment.ScriptTokenStream;
        if (tokens is null || tokens.Count == 0)
        {
            return [];
        }

        var mutations = new List<Mutation>();

        TryAdd(mutations, "identifier-case-flip", RebuildWithTokenEdits(tokens, FlipIdentifierCase));
        TryAdd(mutations, "identifier-bracket-quoting", RebuildWithTokenEdits(tokens, BracketQuoteIdentifier));
        TryAdd(mutations, "comment-injection", RebuildWithTokenEdits(tokens, InjectCommentBeforeKeyword));
        TryAdd(mutations, "blank-line-injection", RebuildWithTokenEdits(tokens, InjectBlankLineBeforeKeyword));

        TryAdd(mutations, "schema-qualification-add", ApplyEdits(sql, CollectSchemaQualificationAddEdits(parseResult.Fragment)));
        TryAdd(mutations, "schema-qualification-remove", ApplyEdits(sql, CollectSchemaQualificationRemoveEdits(parseResult.Fragment)));
        TryAdd(mutations, "redundant-parentheses", ApplyEdits(sql, CollectParenthesizeWhereEdits(parseResult.Fragment)));
        TryAdd(mutations, "operand-order-swap", ApplyEdits(sql, CollectOperandSwapEdits(sql, parseResult.Fragment)));
        TryAdd(mutations, "derived-table-wrap", ApplyEdits(sql, CollectDerivedTableWrapEdits(sql, parseResult.Fragment)));
        TryAdd(mutations, "unrelated-join", ApplyEdits(sql, CollectUnrelatedJoinEdits(parseResult.Fragment)));
        TryAdd(mutations, "alias-rename", ApplyEdits(sql, CollectAliasRenameEdits(parseResult.Fragment)));
        TryAdd(mutations, "alias-drop", ApplyEdits(sql, CollectAliasDropEdits(parseResult.Fragment)));

        return mutations;
    }

    private static string? ApplyEdits(string sql, IReadOnlyList<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return null;
        }

        var ordered = edits.OrderBy(e => e.Start).ToList();
        var result = new System.Text.StringBuilder();
        var pos = 0;

        foreach (var edit in ordered)
        {
            if (edit.Start < pos)
            {
                continue;
            }

            result.Append(sql, pos, edit.Start - pos);
            result.Append(edit.Replacement);
            pos = edit.Start + edit.Length;
        }

        result.Append(sql, pos, sql.Length - pos);
        return result.ToString();
    }

    private static readonly HashSet<string> PseudoTableNames = new(StringComparer.OrdinalIgnoreCase) { "inserted", "deleted" };

    private static List<Edit> CollectSchemaQualificationAddEdits(TSqlFragment fragment)
    {
        var visitor = new SchemaObjectNameVisitor();
        fragment.Accept(visitor);

        var cteVisitor = new CteNameVisitor();
        fragment.Accept(cteVisitor);

        var aliasTargetVisitor = new AliasTargetVisitor();
        fragment.Accept(aliasTargetVisitor);

        return [.. visitor.Names
            .Where(n => n.Identifiers.Count == 1
                && !PseudoTableNames.Contains(n.BaseIdentifier.Value)
                && !cteVisitor.Names.Contains(n.BaseIdentifier.Value)
                && !aliasTargetVisitor.Names.Contains(n))
            .Select(n => new Edit(n.BaseIdentifier.StartOffset, 0, "dbo."))];
    }

    private static List<Edit> CollectSchemaQualificationRemoveEdits(TSqlFragment fragment)
    {
        var visitor = new SchemaObjectNameVisitor();
        fragment.Accept(visitor);

        return [.. visitor.Names
            .Where(n => n.Identifiers.Count == 2 && string.Equals(n.SchemaIdentifier.Value, "dbo", StringComparison.OrdinalIgnoreCase))
            .Select(n => new Edit(n.SchemaIdentifier.StartOffset, n.BaseIdentifier.StartOffset - n.SchemaIdentifier.StartOffset, string.Empty))];
    }

    private static List<Edit> CollectParenthesizeWhereEdits(TSqlFragment fragment)
    {
        var visitor = new WhereClauseVisitor();
        fragment.Accept(visitor);

        var edits = new List<Edit>();
        foreach (var clause in visitor.Clauses)
        {
            var condition = clause.SearchCondition;
            edits.Add(new Edit(condition.StartOffset, 0, "("));
            edits.Add(new Edit(condition.StartOffset + condition.FragmentLength, 0, ")"));
        }

        return edits;
    }

    private static readonly HashSet<BooleanComparisonType> SymmetricComparisonTypes =
    [
        BooleanComparisonType.Equals, BooleanComparisonType.NotEqualToBrackets, BooleanComparisonType.NotEqualToExclamation,
    ];

    private static List<Edit> CollectOperandSwapEdits(string sql, TSqlFragment fragment)
    {
        var whereVisitor = new WhereClauseVisitor();
        fragment.Accept(whereVisitor);

        var edits = new List<Edit>();
        foreach (var clause in whereVisitor.Clauses)
        {
            var comparisonVisitor = new ComparisonVisitor();
            clause.SearchCondition.Accept(comparisonVisitor);

            foreach (var comparison in comparisonVisitor.Comparisons)
            {
                if (!SymmetricComparisonTypes.Contains(comparison.ComparisonType))
                {
                    continue;
                }

                var first = comparison.FirstExpression;
                var second = comparison.SecondExpression;
                if (first is null || second is null)
                {
                    continue;
                }

                var firstText = sql.Substring(first.StartOffset, first.FragmentLength);
                var secondText = sql.Substring(second.StartOffset, second.FragmentLength);
                edits.Add(new Edit(first.StartOffset, first.FragmentLength, secondText));
                edits.Add(new Edit(second.StartOffset, second.FragmentLength, firstText));
            }
        }

        return edits;
    }

    private static List<Edit> CollectDerivedTableWrapEdits(string sql, TSqlFragment fragment)
    {
        var visitor = new SelectStatementVisitor();
        fragment.Accept(visitor);

        var edits = new List<Edit>();
        foreach (var select in visitor.Statements)
        {
            if (select.WithCtesAndXmlNamespaces is not null)
            {
                continue;
            }

            if (select.QueryExpression is not QuerySpecification spec)
            {
                continue;
            }

            if (spec.SelectElements.Any(e => e is SelectSetVariable))
            {
                continue;
            }

            if (select.Into is not null || select.QueryExpression.OrderByClause is not null)
            {
                continue;
            }

            var originalText = sql.Substring(select.StartOffset, select.FragmentLength).TrimEnd();
            var trailingSemicolon = originalText.EndsWith(';') ? ";" : string.Empty;
            if (trailingSemicolon.Length > 0)
            {
                originalText = originalText[..^1].TrimEnd();
            }

            edits.Add(new Edit(select.StartOffset, select.FragmentLength, $"SELECT * FROM ({originalText}) AS MetamorphicWrap{trailingSemicolon}"));
        }

        return edits;
    }

    private static List<Edit> CollectUnrelatedJoinEdits(TSqlFragment fragment)
    {
        var visitor = new FromClauseVisitor();
        fragment.Accept(visitor);

        return [.. visitor.Clauses
            .Select(c => new Edit(c.StartOffset + c.FragmentLength, 0, " CROSS JOIN (SELECT 1 AS MetamorphicJoinCol) AS MetamorphicJoin"))];
    }

    private static List<Edit> CollectAliasRenameEdits(TSqlFragment fragment)
    {
        var aliasVisitor = new TableAliasVisitor();
        fragment.Accept(aliasVisitor);

        var renames = aliasVisitor.Aliases
            .GroupBy(a => a.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .ToList();

        if (renames.Count == 0)
        {
            return [];
        }

        var columnVisitor = new ColumnReferenceVisitor();
        fragment.Accept(columnVisitor);

        var dmlTargetVisitor = new DmlTargetAliasVisitor();
        fragment.Accept(dmlTargetVisitor);

        var edits = new List<Edit>();
        foreach (var alias in renames)
        {
            var newName = alias.Value + "Mm";
            edits.Add(new Edit(alias.StartOffset, alias.FragmentLength, newName));

            foreach (var columnRef in columnVisitor.References)
            {
                var identifiers = columnRef.MultiPartIdentifier?.Identifiers;
                if (identifiers is not { Count: >= 2 })
                {
                    continue;
                }

                var qualifier = identifiers[^2];
                if (string.Equals(qualifier.Value, alias.Value, StringComparison.OrdinalIgnoreCase))
                {
                    edits.Add(new Edit(qualifier.StartOffset, qualifier.FragmentLength, newName));
                }
            }

            foreach (var target in dmlTargetVisitor.BareAliasTargets)
            {
                if (string.Equals(target.Value, alias.Value, StringComparison.OrdinalIgnoreCase))
                {
                    edits.Add(new Edit(target.StartOffset, target.FragmentLength, newName));
                }
            }
        }

        return edits;
    }

    private sealed class TableAliasVisitor : TSqlFragmentVisitor
    {
        public List<Identifier> Aliases { get; } = [];

        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.Alias is { } alias)
            {
                Aliases.Add(alias);
            }

            base.ExplicitVisit(node);
        }
    }

    private static List<Edit> CollectAliasDropEdits(TSqlFragment fragment)
    {
        var aliasedTableVisitor = new AliasedTableReferenceVisitor();
        fragment.Accept(aliasedTableVisitor);

        var candidates = aliasedTableVisitor.References
            .GroupBy(r => r.Alias.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var columnVisitor = new ColumnReferenceVisitor();
        fragment.Accept(columnVisitor);

        var dmlTargetVisitor = new DmlTargetAliasVisitor();
        fragment.Accept(dmlTargetVisitor);

        var edits = new List<Edit>();
        foreach (var (named, alias) in candidates)
        {
            var isReferenced = columnVisitor.References
                .Select(c => c.MultiPartIdentifier?.Identifiers is { Count: >= 2 } identifiers ? identifiers[^2] : null)
                .Concat(dmlTargetVisitor.BareAliasTargets)
                .Any(id => id is not null && string.Equals(id.Value, alias.Value, StringComparison.OrdinalIgnoreCase));

            if (isReferenced)
            {
                continue;
            }

            var dropStart = named.SchemaObject.StartOffset + named.SchemaObject.FragmentLength;
            var dropEnd = named.StartOffset + named.FragmentLength;
            if (dropEnd > dropStart)
            {
                edits.Add(new Edit(dropStart, dropEnd - dropStart, string.Empty));
            }
        }

        return edits;
    }

    private sealed class AliasedTableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<(NamedTableReference Named, Identifier Alias)> References { get; } = [];

        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.Alias is { } alias)
            {
                References.Add((node, alias));
            }

            base.ExplicitVisit(node);
        }
    }

    private sealed class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> References { get; } = [];

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            References.Add(node);
            base.ExplicitVisit(node);
        }
    }

    private sealed class DmlTargetAliasVisitor : TSqlFragmentVisitor
    {
        public List<Identifier> BareAliasTargets { get; } = [];

        public override void ExplicitVisit(UpdateStatement node)
        {
            Record(node.UpdateSpecification.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeleteStatement node)
        {
            Record(node.DeleteSpecification.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(MergeStatement node)
        {
            Record(node.MergeSpecification.Target);
            base.ExplicitVisit(node);
        }

        private void Record(TableReference target)
        {
            if (target is NamedTableReference { Alias: null, SchemaObject.SchemaIdentifier: null } named)
            {
                BareAliasTargets.Add(named.SchemaObject.BaseIdentifier);
            }
        }
    }

    private sealed class SchemaObjectNameVisitor : TSqlFragmentVisitor
    {
        public List<SchemaObjectName> Names { get; } = [];

        public override void ExplicitVisit(SchemaObjectName node)
        {
            Names.Add(node);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SqlDataTypeReference node)
        {
        }

        public override void ExplicitVisit(UserDataTypeReference node)
        {
        }

        public override void ExplicitVisit(XmlDataTypeReference node)
        {
        }
    }

    private sealed class CteNameVisitor : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void ExplicitVisit(CommonTableExpression node)
        {
            Names.Add(node.ExpressionName.Value);
            base.ExplicitVisit(node);
        }
    }

    private sealed class AliasTargetVisitor : TSqlFragmentVisitor
    {
        public HashSet<SchemaObjectName> Names { get; } = [];

        public override void ExplicitVisit(UpdateStatement node) => Record(node.UpdateSpecification.Target, node.UpdateSpecification.FromClause);

        public override void ExplicitVisit(DeleteStatement node) => Record(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause);

        private void Record(TableReference target, FromClause? fromClause)
        {
            if (fromClause is not null && target is NamedTableReference { SchemaObject.SchemaIdentifier: null } named)
            {
                Names.Add(named.SchemaObject);
            }
        }
    }

    private sealed class WhereClauseVisitor : TSqlFragmentVisitor
    {
        public List<WhereClause> Clauses { get; } = [];

        public override void ExplicitVisit(WhereClause node)
        {
            Clauses.Add(node);
            base.ExplicitVisit(node);
        }
    }

    private sealed class ComparisonVisitor : TSqlFragmentVisitor
    {
        public List<BooleanComparisonExpression> Comparisons { get; } = [];

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            Comparisons.Add(node);
            base.ExplicitVisit(node);
        }
    }

    private sealed class SelectStatementVisitor : TSqlFragmentVisitor
    {
        public List<SelectStatement> Statements { get; } = [];

        public override void ExplicitVisit(SelectStatement node)
        {
            Statements.Add(node);
            base.ExplicitVisit(node);
        }
    }

    private sealed class FromClauseVisitor : TSqlFragmentVisitor
    {
        public List<FromClause> Clauses { get; } = [];

        public override void ExplicitVisit(FromClause node)
        {
            Clauses.Add(node);
            base.ExplicitVisit(node);
        }
    }

    private static void TryAdd(List<Mutation> mutations, string name, string? mutatedSql)
    {
        if (mutatedSql is not null && mutatedSql != string.Empty)
        {
            mutations.Add(new Mutation(name, mutatedSql));
        }
    }

    private static string? RebuildWithTokenEdits(IList<TSqlParserToken> tokens, Func<TSqlParserToken, string?> edit)
    {
        var changed = false;
        var result = new System.Text.StringBuilder();

        foreach (var token in tokens)
        {
            var replacement = edit(token);
            if (replacement is not null)
            {
                changed = true;
                result.Append(replacement);
            }
            else
            {
                result.Append(token.Text);
            }
        }

        return changed ? result.ToString() : null;
    }

    private static bool IsPlainIdentifier(TSqlParserToken token) =>
        token.TokenType == TSqlTokenType.Identifier
        && token.Text.Length > 0
        && (char.IsLetter(token.Text[0]) || token.Text[0] == '_')
        && token.Text.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string? FlipIdentifierCase(TSqlParserToken token)
    {
        if (!IsPlainIdentifier(token))
        {
            return null;
        }

        var hasLower = token.Text.Any(char.IsLower);
        var hasUpper = token.Text.Any(char.IsUpper);
        if (hasLower && !hasUpper)
        {
            return token.Text.ToUpperInvariant();
        }

        if (hasUpper && !hasLower)
        {
            return token.Text.ToLowerInvariant();
        }

        return null;
    }

    private static readonly HashSet<string> BuiltInTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "varchar", "nvarchar", "char", "nchar", "varbinary", "binary",
        "decimal", "numeric", "float", "real",
        "int", "bigint", "smallint", "tinyint", "bit",
        "datetime", "datetime2", "date", "time", "datetimeoffset", "smalldatetime",
        "money", "smallmoney", "text", "ntext", "image", "xml",
        "uniqueidentifier", "sql_variant", "hierarchyid", "geography", "geometry",
        "table", "cursor", "max",
    };

    private static readonly HashSet<string> ContextualKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AFTER", "ALLOW_ROW_LOCKS", "ALLOW_PAGE_LOCKS", "ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS",
        "APPLY", "AUTO_CREATE_STATISTICS", "CATCH", "COMPATIBILITY_LEVEL", "CONCAT_NULL_YIELDS_NULL",
        "CURSOR_CLOSE_ON_COMMIT", "DATEFIRST", "DATEFORMAT", "DISABLE", "GENERATED", "IGNORE_DUP_KEY",
        "IMPLICIT_TRANSACTIONS", "INCLUDE", "INSTEAD", "LOCAL", "MASKED", "MEMORY_OPTIMIZED", "NEXT",
        "NORECOMPUTE", "NUMERIC_ROUNDABORT", "OUTPUT", "PARTITION", "PERSISTED", "QUOTED_IDENTIFIER",
        "READ_COMMITTED_SNAPSHOT", "RECOMPILE", "RECURSIVE_TRIGGERS", "RESULT", "RETURNS", "SECURITY",
        "SPATIAL", "SYSTEM_TIME", "TRY", "TYPE", "USING",
        "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT", "COUNT", "SESSION_CONTEXT", "REGEXP_LIKE",
        "READONLY", "FAST_FORWARD", "MATCHED", "IMMEDIATE", "SCHEMABINDING", "RANGE", "DURABILITY",
        "GEOGRAPHY_GRID", "GEOMETRY_GRID", "GEOMETRY_AUTO_GRID", "GEOGRAPHY_AUTO_GRID",
        "SQL_LATIN1_GENERAL_CP1_CI_AS", "LATIN1_GENERAL_CI_AS", "LATIN1_GENERAL_CI_AI",
        "ALWAYS", "ROW", "START", "PERIOD", "POLICY", "FILTER", "PREDICATE", "STATE",
        "SCHEME", "PRIMARY", "SCHEMA_ONLY", "SOURCE", "TARGET", "READCOMMITTEDLOCK", "SETS",
        "SYSTEM_VERSIONING", "HISTORY_TABLE",
    };

    private static string? BracketQuoteIdentifier(TSqlParserToken token) =>
        IsPlainIdentifier(token) && !BuiltInTypeNames.Contains(token.Text) && !ContextualKeywords.Contains(token.Text)
            ? $"[{token.Text}]"
            : null;

    private static readonly HashSet<TSqlTokenType> InjectionKeywords =
    [
        TSqlTokenType.Select, TSqlTokenType.From, TSqlTokenType.Where,
    ];

    private static string? InjectCommentBeforeKeyword(TSqlParserToken token) =>
        InjectionKeywords.Contains(token.TokenType) ? $"/* metamorphic */ {token.Text}" : null;

    private static string? InjectBlankLineBeforeKeyword(TSqlParserToken token) =>
        InjectionKeywords.Contains(token.TokenType) ? $"\n\n{token.Text}" : null;
}

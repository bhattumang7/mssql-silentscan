using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class CheckConstraintScanner
{
    public static IReadOnlyList<CheckConstraintFinding> Scan(DatabaseCatalog catalog)
    {
        var findings = new List<CheckConstraintFinding>();

        foreach (var check in catalog.CheckConstraints)
        {
            AnalyzeCheckConstraint(catalog, check, findings);
        }

        return
        [
            .. findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.ConstraintName, StringComparer.Ordinal)
                .ThenBy(f => f.ColumnName, StringComparer.Ordinal),
        ];
    }

    private static void AnalyzeCheckConstraint(
        DatabaseCatalog catalog, CatalogCheckConstraint check, List<CheckConstraintFinding> findings)
    {
        if (check.IsDisabled || string.IsNullOrWhiteSpace(check.DefinitionText))
        {
            return;
        }

        var table = catalog.Find(check.TableQualifiedName);
        if (table is null)
        {
            return;
        }

        var searchCondition = TryParse(check.DefinitionText, catalog.CompatibilityLevel);
        if (searchCondition is null)
        {
            return;
        }

        var referencedColumnNames = new HashSet<string>(catalog.IdentifierComparer);
        var columnRefVisitor = new ColumnNameCollector(referencedColumnNames);
        searchCondition.Accept(columnRefVisitor);

        var unknownExposedColumnNames = new HashSet<string>(catalog.IdentifierComparer);
        UnknownExposure.Collect(searchCondition, unknownExposedColumnNames);

        var nullGuardedColumnNames = new HashSet<string>(catalog.IdentifierComparer);
        var nullGuardVisitor = new NullGuardCollector(nullGuardedColumnNames);
        searchCondition.Accept(nullGuardVisitor);

        var sourcePath = table.SourcePath;
        var line = table.SourceLine;

        foreach (var columnName in referencedColumnNames)
        {
            var catalogColumn = table.FindColumn(columnName, catalog.IdentifierComparer);
            if (catalogColumn is null)
            {
                continue;
            }

            if (catalogColumn.IsNullable && unknownExposedColumnNames.Contains(columnName) && !nullGuardedColumnNames.Contains(columnName))
            {
                findings.Add(new CheckConstraintFinding(
                    CheckConstraintFindingKind.NullNotHandled, check.ConstraintName, check.TableQualifiedName,
                    catalogColumn.Name, sourcePath, line));
            }
        }
    }

    internal static BooleanExpression? TryParse(string definitionText, int? compatibilityLevel)
    {
        var wrapped = $"SELECT 1 WHERE {definitionText};";
        var result = SqlScriptParser.ParseText("check-constraint.sql", wrapped, initialQuotedIdentifiers: true, compatibilityLevel);
        if (result.HasErrors || result.Fragment is not TSqlScript script)
        {
            return null;
        }

        return script.Batches
            .SelectMany(b => b.Statements)
            .OfType<SelectStatement>()
            .Select(s => s.QueryExpression)
            .OfType<QuerySpecification>()
            .Select(q => q.WhereClause?.SearchCondition)
            .FirstOrDefault(c => c is not null);
    }

    private sealed class ColumnNameCollector(HashSet<string> names) : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            if (identifiers is { Count: > 0 })
            {
                names.Add(identifiers[^1].Value);
            }

            base.ExplicitVisit(node);
        }
    }

    private static class UnknownExposure
    {
        private static readonly HashSet<string> NullPropagatingFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "LEN", "DATALENGTH", "CHARINDEX", "PATINDEX", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "ABS",
            "SUBSTRING", "LEFT", "RIGHT", "REPLACE", "REVERSE", "DATEDIFF", "DATEADD", "DATEPART", "YEAR", "MONTH",
            "DAY", "FLOOR", "CEILING", "ROUND", "SIGN", "SQRT", "POWER",
        };

        public static void Collect(BooleanExpression condition, HashSet<string> names)
        {
            switch (condition)
            {
                case BooleanParenthesisExpression parenthesis:
                    Collect(parenthesis.Expression, names);
                    break;
                case BooleanNotExpression not:
                    Collect(not.Expression, names);
                    break;
                case BooleanBinaryExpression binary:
                    Collect(binary.FirstExpression, names);
                    Collect(binary.SecondExpression, names);
                    break;
                case BooleanComparisonExpression comparison:
                    Collect(comparison.FirstExpression, names);
                    Collect(comparison.SecondExpression, names);
                    break;
                case InPredicate inPredicate:
                    Collect(inPredicate.Expression, names);
                    foreach (var value in inPredicate.Values)
                    {
                        Collect(value, names);
                    }

                    break;
                case LikePredicate like:
                    Collect(like.FirstExpression, names);
                    Collect(like.SecondExpression, names);
                    break;
                case BooleanTernaryExpression ternary:
                    Collect(ternary.FirstExpression, names);
                    Collect(ternary.SecondExpression, names);
                    Collect(ternary.ThirdExpression, names);
                    break;
            }
        }

        private static void Collect(ScalarExpression expression, HashSet<string> names)
        {
            switch (expression)
            {
                case ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers }:
                    names.Add(identifiers[^1].Value);
                    break;
                case ParenthesisExpression parenthesis:
                    Collect(parenthesis.Expression, names);
                    break;
                case UnaryExpression unary:
                    Collect(unary.Expression, names);
                    break;
                case BinaryExpression binary:
                    Collect(binary.FirstExpression, names);
                    Collect(binary.SecondExpression, names);
                    break;
                case CastCall cast:
                    Collect(cast.Parameter, names);
                    break;
                case ConvertCall convert:
                    Collect(convert.Parameter, names);
                    break;
                case FunctionCall { CallTarget: null } function when NullPropagatingFunctions.Contains(function.FunctionName.Value):
                    foreach (var parameter in function.Parameters.Skip(DatePartFunctions.Contains(function.FunctionName.Value) ? 1 : 0))
                    {
                        Collect(parameter, names);
                    }

                    break;
            }
        }

        private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase) { "DATEDIFF", "DATEADD", "DATEPART" };
    }

    private sealed class NullGuardCollector(HashSet<string> names) : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(BooleanIsNullExpression node)
        {
            if (node.Expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers })
            {
                names.Add(identifiers[^1].Value);
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DistinctPredicate node)
        {
            AddDirectColumns([node.FirstExpression, node.SecondExpression]);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CoalesceExpression node)
        {
            AddAllColumns(node.Expressions);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SearchedCaseExpression node)
        {
            if (node.ElseExpression is not null)
            {
                foreach (var whenClause in node.WhenClauses)
                {
                    whenClause.WhenExpression.Accept(new ColumnNameCollector(names));
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(IIfCall node)
        {
            node.Predicate.Accept(new ColumnNameCollector(names));
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FunctionCall node)
        {
            if (node.FunctionName?.Value is { } name)
            {
                if (name.Equals("ISNULL", StringComparison.OrdinalIgnoreCase))
                {
                    AddAllColumns(node.Parameters);
                }
                else if (name.Equals("CONCAT", StringComparison.OrdinalIgnoreCase) || name.Equals("CONCAT_WS", StringComparison.OrdinalIgnoreCase))
                {
                    AddDirectColumns(node.Parameters);
                }
            }

            base.ExplicitVisit(node);
        }

        private void AddAllColumns(IEnumerable<ScalarExpression> expressions)
        {
            foreach (var expression in expressions)
            {
                expression.Accept(new ColumnNameCollector(names));
            }
        }

        private void AddDirectColumns(IEnumerable<ScalarExpression> expressions)
        {
            foreach (var expression in expressions)
            {
                if (expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers })
                {
                    names.Add(identifiers[^1].Value);
                }
            }
        }
    }
}

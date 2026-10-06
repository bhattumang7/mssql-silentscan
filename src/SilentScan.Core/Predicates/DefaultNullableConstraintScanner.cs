using SilentScan.Core.Catalog;

namespace SilentScan.Core.Predicates;

public static class DefaultNullableConstraintScanner
{
    public static IReadOnlyList<DefaultNullableConstraintFinding> Scan(DatabaseCatalog catalog)
    {
        var findings = new List<DefaultNullableConstraintFinding>();

        foreach (var expression in catalog.SchemaExpressions)
        {
            if (expression.Kind != SchemaDependencyKind.DefaultConstraint || expression.ColumnName is not { } columnName)
            {
                continue;
            }

            var column = catalog.Find(expression.TableQualifiedName)?.FindColumn(columnName, catalog.IdentifierComparer);
            if (column is not { IsNullable: true } || IsNullLiteral(expression.DefinitionText))
            {
                continue;
            }

            findings.Add(new DefaultNullableConstraintFinding(
                expression.TableQualifiedName, columnName, expression.DefinitionText, expression.SourcePath, expression.Line));
        }

        return
        [
            .. findings
                .OrderBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.ColumnName, StringComparer.Ordinal),
        ];
    }

    private static bool IsNullLiteral(string definitionText)
    {
        var text = definitionText.AsSpan().Trim();
        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')')
        {
            text = text[1..^1].Trim();
        }

        return text.Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }
}

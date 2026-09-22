using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

internal static class ComputedColumnMatcher
{
    public static bool HasIndexedMatchingComputedColumn(
        DatabaseCatalog catalog, string tableQualifiedName, ScalarExpression predicateExpression)
    {
        foreach (var (_, definitionExpression) in IndexedComputedColumnDefinitions(catalog, tableQualifiedName))
        {
            if (StructurallyEqual(definitionExpression, predicateExpression, catalog.IdentifierComparer))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasIndexedMatchingCastComputedColumn(
        DatabaseCatalog catalog, string tableQualifiedName, string baseColumnName, SqlType explicitType)
    {
        foreach (var (_, definitionExpression) in IndexedComputedColumnDefinitions(catalog, tableQualifiedName))
        {
            var (parameter, dataType) = definitionExpression switch
            {
                CastCall cast => (cast.Parameter, cast.DataType),
                ConvertCall { Style: null } convert => (convert.Parameter, convert.DataType),
                _ => (null, null),
            };

            if (parameter is not null && Unwrap(parameter) is ColumnReferenceExpression columnRef
                && catalog.IdentifierComparer.Equals(LastIdentifier(columnRef), baseColumnName)
                && dataType is not null
                && SqlTypeReferenceResolver.Resolve(dataType, columnCollation: null) is { } resolvedType
                && resolvedType == explicitType)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(string ColumnName, ScalarExpression Definition)> IndexedComputedColumnDefinitions(
        DatabaseCatalog catalog, string tableQualifiedName)
    {
        var table = catalog.Find(tableQualifiedName);
        if (table is null)
        {
            yield break;
        }

        foreach (var expression in catalog.SchemaExpressions)
        {
            if (expression.Kind != SchemaDependencyKind.ComputedColumn
                || expression.ColumnName is not { } computedColumnName
                || !catalog.IdentifierComparer.Equals(expression.TableQualifiedName, tableQualifiedName)
                || !table.IsIndexedColumn(computedColumnName, catalog.IdentifierComparer))
            {
                continue;
            }

            if (TryParseTopLevelExpression(expression.DefinitionText, catalog.CompatibilityLevel) is { } definitionExpression)
            {
                yield return (computedColumnName, definitionExpression);
            }
        }
    }

    private static ScalarExpression? TryParseTopLevelExpression(string definitionText, int? compatibilityLevel)
    {
        var result = SqlScriptParser.ParseText("schema-expression.sql", $"SELECT {definitionText};", initialQuotedIdentifiers: true, compatibilityLevel);
        if (result.HasErrors || result.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { SelectElements: [SelectScalarExpression selectScalar] } } ] }] })
        {
            return null;
        }

        return Unwrap(selectScalar.Expression);
    }

    private static ScalarExpression Unwrap(ScalarExpression expression)
    {
        while (expression is ParenthesisExpression parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool StructurallyEqual(ScalarExpression? a, ScalarExpression? b, StringComparer identifierComparer)
    {
        a = a is null ? null : Unwrap(a);
        b = b is null ? null : Unwrap(b);

        if (TryAsCanonicalDatePart(a) is { } canonicalA)
        {
            a = canonicalA;
        }

        if (TryAsCanonicalDatePart(b) is { } canonicalB)
        {
            b = canonicalB;
        }

        return (a, b) switch
        {
            (null, null) => true,
            (ColumnReferenceExpression ca, ColumnReferenceExpression cb) => identifierComparer.Equals(LastIdentifier(ca), LastIdentifier(cb)),
            (FunctionCall fa, FunctionCall fb) => string.Equals(fa.FunctionName.Value, fb.FunctionName.Value, StringComparison.OrdinalIgnoreCase)
                && fa.Parameters.Count == fb.Parameters.Count
                && fa.Parameters.Zip(fb.Parameters, (x, y) => StructurallyEqual(x, y, identifierComparer)).All(equal => equal),

            (LeftFunctionCall la, LeftFunctionCall lb) => la.Parameters.Count == lb.Parameters.Count
                && la.Parameters.Zip(lb.Parameters, (x, y) => StructurallyEqual(x, y, identifierComparer)).All(equal => equal),
            (CastCall casta, CastCall castb) => TypeEqual(casta.DataType, castb.DataType) && StructurallyEqual(casta.Parameter, castb.Parameter, identifierComparer),
            (ConvertCall converta, ConvertCall convertb) => TypeEqual(converta.DataType, convertb.DataType)
                && StructurallyEqual(converta.Parameter, convertb.Parameter, identifierComparer)
                && StructurallyEqual(converta.Style, convertb.Style, identifierComparer),
            (BinaryExpression ba, BinaryExpression bb) => ba.BinaryExpressionType == bb.BinaryExpressionType
                && StructurallyEqual(ba.FirstExpression, bb.FirstExpression, identifierComparer)
                && StructurallyEqual(ba.SecondExpression, bb.SecondExpression, identifierComparer),
            (StringLiteral sa, StringLiteral sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
            (IntegerLiteral ia, IntegerLiteral ib) => string.Equals(ia.Value, ib.Value, StringComparison.Ordinal),
            (IdentifierLiteral da, IdentifierLiteral db) => string.Equals(NormalizeDatePartUnit(da.Value), NormalizeDatePartUnit(db.Value), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static readonly HashSet<string> DatePartSugarFunctions = new(StringComparer.OrdinalIgnoreCase) { "YEAR", "MONTH", "DAY" };

    private static readonly string[][] DatePartUnitSynonymGroups =
    [
        ["year", "yy", "yyyy"],
        ["quarter", "qq", "q"],
        ["month", "mm", "m"],
        ["dayofyear", "dy", "y"],
        ["day", "dd", "d"],
        ["week", "wk", "ww"],
        ["iso_week", "isowk", "isoww"],
        ["weekday", "dw", "w"],
        ["hour", "hh"],
        ["minute", "mi", "n"],
        ["second", "ss", "s"],
        ["millisecond", "ms"],
        ["microsecond", "mcs"],
        ["nanosecond", "ns"],
        ["tzoffset", "tz"],
    ];

    private static readonly Dictionary<string, string> DatePartUnitSynonyms = DatePartUnitSynonymGroups
        .SelectMany(group => group.Select(synonym => (synonym, canonical: group[0])))
        .ToDictionary(pair => pair.synonym, pair => pair.canonical, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeDatePartUnit(string value) =>
        DatePartUnitSynonyms.TryGetValue(value, out var canonical) ? canonical : value;

    private static FunctionCall? TryAsCanonicalDatePart(ScalarExpression? expression) =>
        expression is FunctionCall { Parameters.Count: 1 } call && DatePartSugarFunctions.Contains(call.FunctionName.Value)
            ? new FunctionCall
            {
                FunctionName = new Identifier { Value = "DATEPART" },
                Parameters = { new IdentifierLiteral { Value = call.FunctionName.Value }, call.Parameters[0] },
            }
            : null;

    private static bool TypeEqual(DataTypeReference a, DataTypeReference b) =>
        SqlTypeReferenceResolver.Resolve(a, columnCollation: null) is { } resolvedA
        && SqlTypeReferenceResolver.Resolve(b, columnCollation: null) is { } resolvedB
        && resolvedA == resolvedB;

    private static string? LastIdentifier(ColumnReferenceExpression columnRef) =>
        columnRef.MultiPartIdentifier?.Identifiers is { Count: > 0 } identifiers ? identifiers[^1].Value : null;
}

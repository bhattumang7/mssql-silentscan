using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class SecurityPredicateIndexScanner
{
    public static IReadOnlyList<SecurityPredicateIndexFinding> Scan(DatabaseCatalog catalog)
    {
        var findings = new List<SecurityPredicateIndexFinding>();

        foreach (var predicate in catalog.SecurityPredicates)
        {
            AnalyzeSecurityPredicate(catalog, predicate, findings);
        }

        return
        [
            .. findings
                .OrderBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.PolicyQualifiedName, StringComparer.Ordinal),
        ];
    }

    private static void AnalyzeSecurityPredicate(
        DatabaseCatalog catalog, CatalogSecurityPredicate predicate, List<SecurityPredicateIndexFinding> findings)
    {
        if (!predicate.IsPolicyEnabled || !predicate.IsFilterPredicate
            || string.IsNullOrWhiteSpace(predicate.PredicateDefinitionText))
        {
            return;
        }

        var table = catalog.Find(predicate.TargetTableQualifiedName);
        if (table is null)
        {
            return;
        }

        var call = TryParseFunctionCall(predicate.PredicateDefinitionText, catalog.CompatibilityLevel);
        if (call is null)
        {
            return;
        }

        var boundColumns = call.Parameters
            .OfType<ColumnReferenceExpression>()
            .Select(c => c.MultiPartIdentifier?.Identifiers is { Count: > 0 } ids ? ids[^1].Value : null)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(catalog.IdentifierComparer)
            .ToList();

        var seekable = SeekableParameters(predicate.PredicateFunctionDefinition, call, catalog);
        if (boundColumns.Count == 0 || seekable.Count == 0)
        {
            return;
        }

        var ledByBoundColumn = table.Indexes.Where(i =>
            !i.IsDisabled && !i.IsFiltered && !i.IsColumnstore && !i.IsJsonIndex && i.KeyColumns.Count > 0
            && boundColumns.Contains(i.KeyColumns[0], catalog.IdentifierComparer))
            .ToList();

        if (ledByBoundColumn.Exists(index => !MustConvertColumn(table, index.KeyColumns[0], seekable, catalog.IdentifierComparer)))
        {
            return;
        }

        findings.Add(new SecurityPredicateIndexFinding(
            predicate.PolicyQualifiedName,
            predicate.TargetTableQualifiedName,
            SchemaObjectNameHelper.QualifyFunctionCall(call),
            boundColumns,
            table.SourcePath,
            table.SourceLine,
            ConvertedIndexName: ledByBoundColumn.Count > 0 ? ledByBoundColumn[0].Name : null));
    }

    private static bool MustConvertColumn(
        CatalogTable table, string columnName, Dictionary<string, ProcedureParameter> seekable, StringComparer comparer)
    {
        if (!seekable.TryGetValue(columnName, out var parameter)
            || table.FindColumn(columnName, comparer)?.Type is not { IsStringFamily: true } column
            || parameter.DataType is not SqlDataTypeReference { SqlDataTypeOption: var parameterType } declared
            || parameterType is not (SqlDataTypeOption.NVarChar or SqlDataTypeOption.NChar))
        {
            return false;
        }

        if (!column.IsUnicodeString)
        {
            return true;
        }

        var parameterLength = declared.Parameters is [var length]
            ? int.TryParse(length.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue
            : 1;
        return column.Length > parameterLength;
    }

    private static readonly HashSet<string> NationalCharacterSessionFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "USER_NAME", "SUSER_SNAME", "SUSER_NAME", "ORIGINAL_LOGIN",
    };

    private static Dictionary<string, ProcedureParameter> SeekableParameters(string? definition, FunctionCall call, DatabaseCatalog catalog)
    {
        var seekable = new Dictionary<string, ProcedureParameter>(catalog.IdentifierComparer);
        if (string.IsNullOrWhiteSpace(definition))
        {
            return seekable;
        }

        var parsed = SqlScriptParser.ParseText("security-predicate-function.sql", definition, initialQuotedIdentifiers: true, catalog.CompatibilityLevel);
        if (parsed.HasErrors
            || parsed.Fragment is not TSqlScript { Batches: [{ Statements: [CreateFunctionStatement { ReturnType: SelectFunctionReturnType { SelectStatement.QueryExpression: QuerySpecification specification } } function] }] })
        {
            return seekable;
        }

        var conjuncts = PredicateTreeWalker.FlattenAnd(specification.WhereClause?.SearchCondition).ToList();
        for (var i = 0; i < call.Parameters.Count && i < function.Parameters.Count; i++)
        {
            var parameter = function.Parameters[i];
            if (call.Parameters[i] is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { Value: { } column }] }
                && conjuncts.Exists(c => IsSeekableEquality(c, parameter, catalog.IdentifierComparer)))
            {
                seekable.TryAdd(column, parameter);
            }
        }

        return seekable;
    }

    private static bool IsSeekableEquality(BooleanExpression conjunct, ProcedureParameter parameter, StringComparer comparer)
    {
        if (conjunct is not BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } comparison)
        {
            return false;
        }

        var (variable, other) = (false, (ScalarExpression)null!);
        if (comparison.FirstExpression is VariableReference first && comparer.Equals(first.Name, parameter.VariableName.Value))
        {
            (variable, other) = (true, comparison.SecondExpression);
        }
        else if (comparison.SecondExpression is VariableReference second && comparer.Equals(second.Name, parameter.VariableName.Value))
        {
            (variable, other) = (true, comparison.FirstExpression);
        }

        return variable && parameter.DataType is SqlDataTypeReference { SqlDataTypeOption: var parameterType } && IsRuntimeConstantOfSameType(other, parameterType);
    }

    private static bool IsRuntimeConstantOfSameType(ScalarExpression expression, SqlDataTypeOption parameterType)
    {
        while (expression is ParenthesisExpression parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression switch
        {
            CastCall { Parameter: FunctionCall inner, DataType: SqlDataTypeReference target } =>
                IsSessionContext(inner) && target.SqlDataTypeOption == parameterType,
            ConvertCall { Parameter: FunctionCall inner, Style: null, DataType: SqlDataTypeReference target } =>
                IsSessionContext(inner) && target.SqlDataTypeOption == parameterType,
            FunctionCall { Parameters.Count: 0 } sessionFunction =>
                NationalCharacterSessionFunctions.Contains(sessionFunction.FunctionName.Value) && parameterType == SqlDataTypeOption.NVarChar,
            _ => false,
        };
    }

    private static bool IsSessionContext(FunctionCall call) =>
        string.Equals(call.FunctionName.Value, "SESSION_CONTEXT", StringComparison.OrdinalIgnoreCase);

    private static FunctionCall? TryParseFunctionCall(string definitionText, int? compatibilityLevel)
    {
        var result = SqlScriptParser.ParseText("security-predicate.sql", $"SELECT {definitionText};", initialQuotedIdentifiers: true, compatibilityLevel);
        if (result.HasErrors
            || result.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { SelectElements: [SelectScalarExpression selectScalar] } }] }] })
        {
            return null;
        }

        var expression = selectScalar.Expression;
        while (expression is ParenthesisExpression parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression as FunctionCall;
    }
}

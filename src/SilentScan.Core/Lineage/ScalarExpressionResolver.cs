using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Lineage;

/// <summary>Resolves a single SELECT-list scalar expression to its <see cref="ColumnProvenance"/>.</summary>
public static class ScalarExpressionResolver
{
    public static ColumnProvenance Resolve(
        ScalarExpression expression, IReadOnlyDictionary<string, ResolvedRelation> scope, IReadOnlyList<ResolvedRelation> orderedRelations) =>
        expression switch
        {
            ColumnReferenceExpression columnRef => ResolveColumnReference(columnRef, scope, orderedRelations),
            CastCall castCall => ResolveExplicitType(castCall.DataType),
            ConvertCall convertCall => ResolveExplicitType(convertCall.DataType),
            Literal literal => new ColumnProvenance.Expression(LiteralTypeResolver.Resolve(literal)),
            _ => new ColumnProvenance.Expression(InferredType: null),
        };

    private static ColumnProvenance ResolveExplicitType(DataTypeReference dataType)
    {
        var resolved = SqlTypeReferenceResolver.Resolve(dataType, columnCollation: null);
        return resolved is { } type
            ? new ColumnProvenance.Cast(type)
            : new ColumnProvenance.Unknown("CAST/CONVERT target type could not be resolved");
    }

    private static ColumnProvenance ResolveColumnReference(
        ColumnReferenceExpression columnRef, IReadOnlyDictionary<string, ResolvedRelation> scope, IReadOnlyList<ResolvedRelation> orderedRelations)
    {
        var identifiers = columnRef.MultiPartIdentifier.Identifiers;
        var columnName = identifiers[^1].Value;

        if (identifiers.Count >= 2)
        {
            var qualifier = identifiers[^2].Value;
            if (!scope.TryGetValue(qualifier, out var relation))
            {
                return new ColumnProvenance.Unknown($"unknown table alias '{qualifier}'");
            }

            return relation.FindColumn(columnName)?.Provenance
                ?? new ColumnProvenance.Unknown($"column '{columnName}' not found on '{qualifier}'");
        }

        var matches = orderedRelations
            .Select(r => r.FindColumn(columnName))
            .Where(c => c is not null)
            .ToList();

        return matches.Count switch
        {
            0 => new ColumnProvenance.Unknown($"column '{columnName}' not found in FROM scope"),
            > 1 => new ColumnProvenance.Unknown($"column '{columnName}' is ambiguous across the FROM scope"),
            _ => matches[0]!.Provenance,
        };
    }
}

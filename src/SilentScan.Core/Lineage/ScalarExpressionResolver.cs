using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Lineage;

/// <summary>Resolves a single SELECT-list scalar expression to its <see cref="ColumnProvenance"/>.</summary>
public static class ScalarExpressionResolver
{
    public static ColumnProvenance Resolve(
        ScalarExpression expression, IReadOnlyDictionary<string, ScopeEntry> scope, IReadOnlyList<ScopeEntry> orderedRelations, string sourcePath) =>
        expression switch
        {
            ColumnReferenceExpression columnRef => ResolveColumnReference(columnRef, scope, orderedRelations),
            CastCall castCall => ResolveExplicitType(castCall.DataType, sourcePath, castCall.StartLine),
            ConvertCall convertCall => ResolveExplicitType(convertCall.DataType, sourcePath, convertCall.StartLine),
            Literal literal => new ColumnProvenance.Expression(LiteralTypeResolver.Resolve(literal)),
            _ => new ColumnProvenance.Expression(InferredType: null),
        };

    private static ColumnProvenance ResolveExplicitType(DataTypeReference dataType, string sourcePath, int line)
    {
        var resolved = SqlTypeReferenceResolver.Resolve(dataType, columnCollation: null);
        return resolved is { } type
            ? new ColumnProvenance.Cast(type, sourcePath, line)
            : new ColumnProvenance.Unknown("CAST/CONVERT target type could not be resolved");
    }

    private static ColumnProvenance ResolveColumnReference(
        ColumnReferenceExpression columnRef, IReadOnlyDictionary<string, ScopeEntry> scope, IReadOnlyList<ScopeEntry> orderedRelations)
    {
        var identifiers = columnRef.MultiPartIdentifier.Identifiers;
        var columnName = identifiers[^1].Value;

        if (identifiers.Count >= 2)
        {
            var qualifier = identifiers[^2].Value;
            if (!scope.TryGetValue(qualifier, out var entry))
            {
                return new ColumnProvenance.Unknown($"unknown table alias '{qualifier}'");
            }

            var column = entry.Relation.FindColumn(columnName);
            return column is null
                ? new ColumnProvenance.Unknown($"column '{columnName}' not found on '{qualifier}'")
                : BumpDepthIfViewLayer(column.Provenance, entry.IsViewLayer);
        }

        var matches = orderedRelations
            .Select(entry => (Entry: entry, Column: entry.Relation.FindColumn(columnName)))
            .Where(m => m.Column is not null)
            .ToList();

        return matches.Count switch
        {
            0 => new ColumnProvenance.Unknown($"column '{columnName}' not found in FROM scope"),
            > 1 => new ColumnProvenance.Unknown($"column '{columnName}' is ambiguous across the FROM scope"),
            _ => BumpDepthIfViewLayer(matches[0].Column!.Provenance, matches[0].Entry.IsViewLayer),
        };
    }

    internal static ColumnProvenance BumpDepthIfViewLayer(ColumnProvenance provenance, bool isViewLayer)
    {
        if (!isViewLayer)
        {
            return provenance;
        }

        return provenance switch
        {
            ColumnProvenance.BaseColumn bc => bc with { Depth = bc.Depth + 1 },
            ColumnProvenance.Cast cast => cast with { Depth = cast.Depth + 1 },
            _ => provenance,
        };
    }
}

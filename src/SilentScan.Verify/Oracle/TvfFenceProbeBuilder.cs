using SilentScan.Core.Catalog;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Verify.Oracle;

public static class TvfFenceProbeBuilder
{
    public static string? BuildFunctionProbe(TvfFenceFinding finding, IReadOnlyList<SqlType>? parameterTypes)
    {
        if (finding.FunctionQualifiedName is not { } qualifiedName || parameterTypes is null)
        {
            return null;
        }

        var arguments = new List<string>(parameterTypes.Count);
        foreach (var type in parameterTypes)
        {
            var typeSyntax = SqlTypeSyntaxFormatter.Format(type);
            if (typeSyntax is null)
            {
                return null;
            }

            arguments.Add($"CAST(NULL AS {typeSyntax})");
        }

        return $"SELECT * FROM {BracketQualifiedName(qualifiedName)}({string.Join(", ", arguments)});";
    }

    private static string BracketQualifiedName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.', 2);
        return parts.Length == 2 ? $"{Bracket(parts[0])}.{Bracket(parts[1])}" : Bracket(parts[0]);
    }

    private static string Bracket(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

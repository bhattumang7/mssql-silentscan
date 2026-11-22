using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class DeclaredColumnTypeProbe
{
    public static Perturbation Describe(string? callerModule, string? column, string? declaredType, string? describedType)
    {
        if (callerModule is null || column is null || declaredType is null || describedType is null)
        {
            return Perturbation.NotApplicable("The finding does not name the caller, the column and both types.");
        }

        var bareType = Bare(declaredType);
        var bareDescribed = Bare(describedType);
        var tokens = Regex.Matches(bareType, @"\w+|[^\w\s]").Select(match => Regex.Escape(match.Value));
        var declaration = new Regex(
            @"(\[?" + Regex.Escape(column) + @"\]?\s+)" + string.Join(@"\s*", tokens),
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                callerModule,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, "${1}" + bareDescribed, 1);
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, callerModule, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }

    private static string Bare(string typeText) =>
        Regex.Replace(typeText, @"\s+COLLATE\s+\S+|\s+(?:NOT\s+)?NULL\s*$", string.Empty, RegexOptions.IgnoreCase);
}

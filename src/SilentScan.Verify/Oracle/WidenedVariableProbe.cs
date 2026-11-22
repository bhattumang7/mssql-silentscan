using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class WidenedVariableProbe
{
    public static Perturbation Describe(string? tableQualifiedName, string variable, string modulePath, string typePattern, Func<string, string> widen)
    {
        if (tableQualifiedName is not null || !variable.StartsWith('@'))
        {
            return Perturbation.NotApplicable("The finding does not name a variable target.");
        }

        var declaration = new Regex(
            @"(\bDECLARE\s+" + Regex.Escape(variable) + @"\s+(?:AS\s+)?)(" + typePattern + ")",
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                modulePath,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, match => match.Groups[1].Value + widen(match.Groups[2].Value));
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, modulePath, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }
}

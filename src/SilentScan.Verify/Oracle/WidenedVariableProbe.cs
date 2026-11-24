using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class WidenedVariableProbe
{
    public static Perturbation Describe(string target, string name, string modulePath, string typePattern, Func<string, string> widen)
    {
        var head = DeclarationHead(target, name);
        if (head is null)
        {
            return Perturbation.NotApplicable("The finding does not name a variable, parameter or return target.");
        }

        var declaration = new Regex("(" + head + ")(" + typePattern + ")" + (target == "ParameterDefault" ? @"(?=\s*=)" : string.Empty), RegexOptions.IgnoreCase);
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

    private static string? DeclarationHead(string target, string name)
    {
        var escaped = Regex.Escape(name);
        return target switch
        {
            "Variable" when name.StartsWith('@') => @"\bDECLARE\s+" + escaped + @"\s+(?:AS\s+)?",
            "ParameterDefault" => @"(?<!\bDECLARE\s+)" + escaped + @"\s+(?:AS\s+)?",
            "FunctionReturn" when name == "RETURN" => @"\bRETURNS\s+",
            _ => null,
        };
    }
}

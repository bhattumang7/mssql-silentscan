using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class CalleeParameterWidening
{
    public static Perturbation Describe(string calleeQualifiedName, string formalParameterName, string formalParameterTypeDisplay)
    {
        if (!VerifierRegex.IsMatch(formalParameterTypeDisplay, @"^(?:n?var)?char\s*\(|^n?char\s*\(|^(?:var)?binary\s*\(", RegexOptions.IgnoreCase))
        {
            return Perturbation.NotApplicable("The probe handles input string and binary parameters only.");
        }

        var declaration = VerifierRegex.Create(
            @"(" + Regex.Escape(formalParameterName) + @"\s+(?:AS\s+)?)(n?varchar|n?char|varbinary|binary)\s*\(\s*\d+\s*\)",
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                calleeQualifiedName,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, match => match.Groups[1].Value + match.Groups[2].Value + "(MAX)", 1);
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, calleeQualifiedName, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }
}

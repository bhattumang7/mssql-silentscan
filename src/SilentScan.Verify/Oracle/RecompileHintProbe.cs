using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class RecompileHintProbe
{
    public static Perturbation Describe(string detailText, string sourcePath)
    {
        var variable = detailText.Split(' ', 2)[0];
        var statement = VerifierRegex.Create(
            @"(\bSELECT\b(?:(?!OPTION)[^;])*" + Regex.Escape(variable) + @"\b(?:(?!OPTION)[^;])*)(;)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.EstimatesChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, sourcePath, string.Empty, definition => statement.Replace(definition, "$1 OPTION (RECOMPILE)$2", 1), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
    }
}

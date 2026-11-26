using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class SetOptionRestoreProbe
{
    public static Perturbation Describe(string moduleQualifiedName, Regex setting, string replacement) =>
        new(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, moduleQualifiedName, string.Empty, definition => setting.Replace(definition, replacement), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
}

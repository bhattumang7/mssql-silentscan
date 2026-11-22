using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class ModuleCompileFlagScanner
{
    public static IReadOnlyList<ModuleCompileFlagFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var moduleQualifiedName = parseResult.SourcePath;
        var findings = new List<ModuleCompileFlagFinding>();

        var schemaBound = catalog.TryGetModuleIsSchemaBound(moduleQualifiedName, out var isSchemaBound) && isSchemaBound;

        if (!schemaBound
            && catalog.TryGetModuleUsesDatabaseCollation(moduleQualifiedName, out var usesDatabaseCollation)
            && usesDatabaseCollation)
        {
            findings.Add(new ModuleCompileFlagFinding(
                ModuleCompileFlagFindingKind.TableValuedFunctionReturnUsesDatabaseCollation, moduleQualifiedName, parseResult.SourcePath,
                parseResult.Fragment.StartLine, parseResult.Fragment.StartColumn));
        }

        return findings;
    }
}

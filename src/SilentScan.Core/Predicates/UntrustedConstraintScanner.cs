using SilentScan.Core.Catalog;

namespace SilentScan.Core.Predicates;

public static class UntrustedConstraintScanner
{
    public static IReadOnlyList<UntrustedConstraintFinding> Scan(DatabaseCatalog catalog)
    {
        var findings = new List<UntrustedConstraintFinding>();

        foreach (var check in catalog.CheckConstraints.Where(c => c.IsNotTrusted && !c.IsDisabled))
        {
            var table = catalog.Find(check.TableQualifiedName);
            findings.Add(new UntrustedConstraintFinding(
                UntrustedConstraintFindingKind.CheckConstraint, check.ConstraintName, check.TableQualifiedName,
                table?.SourcePath ?? check.TableQualifiedName, table?.SourceLine ?? 0));
        }

        return
        [
            .. findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.ConstraintName, StringComparer.Ordinal),
        ];
    }
}

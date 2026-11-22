using SilentScan.Core.Catalog;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class AnsiPaddingOffColumnScanner
{
    private static bool TrimsTrailingPadding(CatalogColumn column) => column.Type is { } type
        && (type.Category is SqlTypeCategory.VarChar or SqlTypeCategory.NVarChar or SqlTypeCategory.VarBinary
            || column.IsNullable && type.Category is SqlTypeCategory.Char or SqlTypeCategory.Binary);

    public static IReadOnlyList<AnsiPaddingOffColumnFinding> Scan(DatabaseCatalog catalog)
    {
        var findings = new List<AnsiPaddingOffColumnFinding>();

        foreach (var table in catalog.Tables)
        {
            foreach (var column in table.Columns)
            {
                if (column.IsAnsiPadded)
                {
                    continue;
                }

                if (!TrimsTrailingPadding(column))
                {
                    continue;
                }

                findings.Add(new AnsiPaddingOffColumnFinding(
                    table.QualifiedName, column.Name, table.SourcePath, table.SourceLine));
            }
        }

        return
        [
            .. findings
                .OrderBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.ColumnName, StringComparer.Ordinal),
        ];
    }
}

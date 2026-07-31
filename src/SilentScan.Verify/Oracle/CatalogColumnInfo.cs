namespace SilentScan.Verify.Oracle;

/// <summary>
/// One row read back from sys.columns/sys.types for a deployed table or view - the free
/// ground-truth oracle for the lineage engine (CLAUDE.md: "diff our inferred view column
/// types/collations against sys.columns for every view; ANY mismatch is a P0 bug").
/// </summary>
public sealed record CatalogColumnInfo(
    string ColumnName,
    string TypeName,
    short MaxLength,
    byte Precision,
    byte Scale,
    string? CollationName,
    bool IsNullable);

using SilentScan.Core.Catalog;

namespace SilentScan.Core.Lineage;

/// <summary>
/// A multi-statement TVF's RETURNS @t TABLE(...) declaration. CLAUDE.md: "Multi-statement
/// TVFs read declared RETURNS table types" - these columns are declared, not traced through
/// a query body, so they resolve directly to <see cref="ColumnProvenance.Declared"/>.
/// </summary>
public sealed record MultiStatementTvfDefinition(
    string QualifiedName,
    IReadOnlyList<CatalogColumn> Columns,
    string SourcePath,
    int SourceLine);

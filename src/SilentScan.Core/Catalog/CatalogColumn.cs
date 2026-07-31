namespace SilentScan.Core.Catalog;

/// <summary>A column as declared in DDL. <see cref="Type"/> is null when the declared type couldn't be resolved (e.g. a user-defined type) - callers must treat that as UNKNOWN, never guess.</summary>
public sealed record CatalogColumn(
    string Name,
    SqlType? Type,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsPersisted);

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Lineage;

/// <summary>A CREATE VIEW or inline TVF, extracted from parsed source (CLAUDE.md: "Inline TVFs = views").</summary>
public sealed record ViewDefinition(
    string QualifiedName,
    SelectStatement SelectStatement,
    IReadOnlyList<string>? ExplicitColumnNames,
    string SourcePath,
    int SourceLine);

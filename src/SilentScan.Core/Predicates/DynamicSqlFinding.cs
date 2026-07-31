namespace SilentScan.Core.Predicates;

/// <summary>
/// An EXEC(@sql)/EXEC('...')/sp_executesql call site. CLAUDE.md dynamic SQL policy: never
/// silently count these as clean - IsLiteralOnly distinguishes the (rare) case we could in
/// principle parse (a single string literal, no concatenation/variables) from the general
/// unanalyzable case, which must be reported ("X% of procs contain dynamic SQL we could not
/// analyze"), not swallowed.
/// </summary>
public sealed record DynamicSqlFinding(string SourcePath, int Line, bool IsLiteralOnly);

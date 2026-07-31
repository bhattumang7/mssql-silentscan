using System.CommandLine;

namespace SilentScan.Verify.Commands;

/// <summary>
/// Root command for the verification tool (CLAUDE.md SilentScan.Verify): deploys corpus DDL
/// to the Docker SQL Server oracle, diffs inferred view column types against sys.columns,
/// and confirms SCAN_FORCED findings via CONVERT_IMPLICIT in plan XML. The `deploy`/`diff`/
/// `confirm` subcommands land with the Phase 2 lineage engine (plan.md) that they verify
/// against; until then this exposes only its description, not functionality it doesn't have.
/// </summary>
public static class VerifyRootCommand
{
    public static RootCommand Create() =>
        new("silentscan-verify — deploys corpus DDL to a disposable SQL Server and confirms findings against sys.columns and plan XML.");
}

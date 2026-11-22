using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SecurityPredicateIndexRuleId)]
public sealed class SecurityPredicateIndexPairedOracleTests : RuleOracle
{
    private const string Prefix = """
        CREATE SCHEMA Sec;
        GO
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, TenantId INT NOT NULL, Pad CHAR(50) NOT NULL DEFAULT 'x');
        GO
        INSERT dbo.T (Id, TenantId) SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), ROW_NUMBER() OVER (ORDER BY (SELECT 1)) % 500 FROM sys.all_objects a, sys.all_objects b;
        GO
        """;

    private const string Suffix = """
        CREATE FUNCTION Sec.fnEquality(@TenantId INT) RETURNS TABLE WITH SCHEMABINDING AS
        RETURN SELECT 1 AS Allowed WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS INT);
        GO
        CREATE SECURITY POLICY Sec.TPolicy ADD FILTER PREDICATE Sec.fnEquality(TenantId) ON dbo.T WITH (STATE = ON);
        GO
        """;

    private const string Unindexed = Prefix + "\n" + Suffix;

    private const string Indexed = Prefix + "\nCREATE INDEX IX_T_Tenant ON dbo.T (TenantId);\nGO\n" + Suffix;

    private const string Secured = "unindexed-secured-table";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SecurityPredicateIndexFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((SecurityPredicateIndexFinding)finding) with { FilteredColumns = [] },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Secured, Unindexed, "SELECT Pad FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("indexed-secured-table", Indexed, "SELECT Pad FROM dbo.T;"), Secured, OwnSetup: true),
    ];
}

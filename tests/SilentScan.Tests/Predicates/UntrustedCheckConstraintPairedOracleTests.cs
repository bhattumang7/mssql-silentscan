using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.UntrustedCheckConstraintRuleId)]
public sealed class UntrustedCheckConstraintPairedOracleTests : RuleOracle
{
    private const string Members = """
        CREATE TABLE dbo.Orders_Open (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(10) NOT NULL
            CONSTRAINT CK_Orders_Open_Status CHECK (Status = 'Open'));
        CREATE TABLE dbo.Orders_Closed (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(10) NOT NULL
            CONSTRAINT CK_Orders_Closed_Status CHECK (Status = 'Closed'));
        INSERT INTO dbo.Orders_Open (OrderId, Status) VALUES (1, 'Open');
        INSERT INTO dbo.Orders_Closed (OrderId, Status) VALUES (2, 'Closed');
        GO
        CREATE VIEW dbo.Orders AS
            SELECT OrderId, Status FROM dbo.Orders_Open
            UNION ALL
            SELECT OrderId, Status FROM dbo.Orders_Closed;
        GO
        """;

    private const string Untrusted = Members + "\nALTER TABLE dbo.Orders_Closed NOCHECK CONSTRAINT CK_Orders_Closed_Status;\nALTER TABLE dbo.Orders_Closed WITH NOCHECK CHECK CONSTRAINT CK_Orders_Closed_Status;\nGO\n";

    private const string NotTrusted = "check-constraint-disabled-then-reenabled-with-nocheck";

    private const string Statement = "SELECT OrderId FROM dbo.Orders WHERE Status = 'Open';";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((UntrustedConstraintFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((UntrustedConstraintFinding)finding) with { ConstraintName = "CK_Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(NotTrusted, Untrusted, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("check-constraint-trusted", Members, Statement), NotTrusted, OwnSetup: true),
    ];
}

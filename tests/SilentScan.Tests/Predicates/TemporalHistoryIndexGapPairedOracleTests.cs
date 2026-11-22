using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TemporalTableHistoryIndexGapRuleId)]
public sealed class TemporalHistoryIndexGapPairedOracleTests : RuleOracle
{
    private const string Prefix = """
        CREATE TABLE dbo.T
        (
            Id INT NOT NULL PRIMARY KEY,
            A INT NOT NULL,
            Pad CHAR(100) NOT NULL DEFAULT 'x',
            ValidFrom DATETIME2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
            ValidTo DATETIME2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
            PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
        )
        WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.TH));
        GO
        CREATE INDEX IX_T_A ON dbo.T (A);
        GO
        INSERT dbo.T (Id, A) SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), ROW_NUMBER() OVER (ORDER BY (SELECT 1)) % 600 FROM sys.all_objects a, sys.all_objects b;
        GO
        UPDATE dbo.T SET Pad = 'y';
        GO
        """;

    private const string Gap = "history-without-matching-index";

    private const string Statement = "SELECT A FROM [dbo].[T] FOR SYSTEM_TIME ALL WHERE A = 3;";

    protected override int? MinimumCompatibilityLevel => 130;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((TemporalTableHistoryIndexGapFinding)finding) with { HistoryTableQualifiedName = "dbo.Other" },
        finding => ((TemporalTableHistoryIndexGapFinding)finding) with { KeyColumns = [] },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Gap, Prefix, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("history-with-matching-index", Prefix + "\nCREATE INDEX IX_TH_A ON dbo.TH (A);\nGO\n", Statement), Gap, OwnSetup: true),
    ];
}

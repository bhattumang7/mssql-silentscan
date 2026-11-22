using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.UnderLengthParameterRuleId)]
public sealed class UnderLengthParameterPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
        GO
        INSERT INTO dbo.T (Id, Code) VALUES (1, 'abc'), (2, 'abcde');
        GO
        """;

    private const string Short = Base + "\nCREATE PROCEDURE dbo.P @p VARCHAR(3) AS SELECT Id FROM dbo.T WHERE Code = @p;\nGO\n";

    private const string Full = Base + "\nCREATE PROCEDURE dbo.P @p VARCHAR(10) AS SELECT Id FROM dbo.T WHERE Code = @p;\nGO\n";

    private const string Truncating = "parameter-shorter-than-the-column";

    private const string Statement = "EXEC dbo.P @p = 'abcde';";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((UnderLengthParameterFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((UnderLengthParameterFinding)finding) with { OtherOperandLength = 7 },
        finding => ((UnderLengthParameterFinding)finding) with { VariableName = "@other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Short, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("parameter-as-long-as-the-column", Full, Statement), Truncating, OwnSetup: true),
    ];
}

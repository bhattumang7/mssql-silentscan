using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

public abstract class ForcedParameterizationPairedOracle : RuleOracle
{
    protected const string Setup = """
        ALTER DATABASE CURRENT SET PARAMETERIZATION FORCED;
        GO
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(40) NOT NULL, Val INT NOT NULL, D DATETIME NOT NULL DEFAULT '20200101');
        GO
        INSERT INTO dbo.T (Id, Name, Val) VALUES (1, 'abc', 1), (2, 'abd', 2);
        GO
        """;

    protected override IReadOnlyList<string> ScannerOnlyForms => [StatementForms.Procedure, StatementForms.View];

    protected abstract string AbsentLiteral { get; }

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations =>
    [
        finding => ((ForcedParameterizationFinding)finding) with { LiteralSourceText = AbsentLiteral },
        finding => ((ForcedParameterizationFinding)finding) with { LiteralSourceText = null },
    ];
}

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

    protected static string Splicing(string assembledText) =>
        $"CREATE PROCEDURE dbo.Run @v INT AS BEGIN DECLARE @sql NVARCHAR(MAX) = {assembledText}; EXEC (@sql); END;";

    protected override IReadOnlyList<string> ScannerOnlyForms => [StatementForms.DynamicSql];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations =>
    [
        finding => ((ForcedParameterizationFinding)finding) with { LiteralSourceText = "'absent__silentscan_sym_L1C1__'" },
        finding => ((ForcedParameterizationFinding)finding) with { LiteralSourceText = null },
    ];
}

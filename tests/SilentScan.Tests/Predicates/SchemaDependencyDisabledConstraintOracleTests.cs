using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ScalarUdfSchemaDependencyRuleId)]
public sealed class SchemaDependencyDisabledConstraintOracleTests
{
    [Fact]
    public async Task OnlyAnEnabledCheckConstraintCallingAScalarUdfIsReported()
    {
        var report = await EngineAuthoritativeScan.ScanAsync("""
            CREATE FUNCTION dbo.Positive (@x INT) RETURNS INT WITH SCHEMABINDING AS BEGIN DECLARE @i INT = 0; WHILE @i < 1 SET @i += 1; RETURN @x; END;
            GO
            CREATE TABLE dbo.Enforced (A INT NOT NULL CONSTRAINT CK_Enforced CHECK (dbo.Positive(A) >= 0));
            CREATE TABLE dbo.Switched (A INT NOT NULL CONSTRAINT CK_Switched CHECK (dbo.Positive(A) >= 0));
            GO
            ALTER TABLE dbo.Switched NOCHECK CONSTRAINT CK_Switched;
            GO
            """);

        var tables = report.FindingsByRuleId.Values.SelectMany(findings => findings)
            .OfType<SilentScan.Core.Predicates.ScalarUdfFinding>()
            .Where(f => f.Kind == SilentScan.Core.Predicates.ScalarUdfFindingKind.SchemaDependency)
            .Select(f => f.ReferencedObjectQualifiedName)
            .ToList();

        Assert.Equal(["dbo.Enforced"], tables);
    }
}

using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossExactToApproximateRuleId)]
public sealed class WriteLossExactToApproximatePairedOracleTests : RuleOracle
{
    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new("value-assigned-to-a-float-variable", "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src BIGINT = 9007199254740993; DECLARE @v FLOAT; SET @v = @src; SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"),
        new("value-assigned-to-a-real-variable", "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src INT = 16777217; DECLARE @v REAL; SET @v = @src; SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"),
        new("value-written-to-an-approximate-column", "CREATE TABLE dbo.T (C FLOAT NULL);\nGO\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DECIMAL(19,4) = 123456789012345.6789; INSERT INTO dbo.T (C) VALUES (@src); SELECT CAST(C AS DECIMAL(38,4)) AS V FROM dbo.T; END\nGO\n", "EXEC dbo.P;"),
        new("value-returned-through-an-approximate-return-type", "CREATE FUNCTION dbo.F() RETURNS FLOAT AS BEGIN DECLARE @src BIGINT = 9007199254740993; RETURN @src; END\nGO\n", "SELECT CAST(dbo.F() AS DECIMAL(38,4)) AS V;"),
        new("default-value-wider-than-the-approximate-parameter", "CREATE PROCEDURE dbo.P @v FLOAT = 9007199254740993 AS BEGIN SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-float-variable", "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src INT = 2147483647; DECLARE @v FLOAT; SET @v = @src; SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"), "value-assigned-to-a-float-variable", OwnSetup: true),
        new(new("value-fits-the-real-variable", "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src SMALLINT = 32767; DECLARE @v REAL; SET @v = @src; SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"), "value-assigned-to-a-real-variable", OwnSetup: true),
        new(new("value-fits-the-approximate-column", "CREATE TABLE dbo.T (C FLOAT NULL);\nGO\nCREATE PROCEDURE dbo.P AS BEGIN DECLARE @src INT = 2147483647; INSERT INTO dbo.T (C) VALUES (@src); SELECT CAST(C AS DECIMAL(38,4)) AS V FROM dbo.T; END\nGO\n", "EXEC dbo.P;"), "value-written-to-an-approximate-column", OwnSetup: true),
        new(new("value-fits-the-approximate-return-type", "CREATE FUNCTION dbo.F() RETURNS FLOAT AS BEGIN DECLARE @src INT = 2147483647; RETURN @src; END\nGO\n", "SELECT CAST(dbo.F() AS DECIMAL(38,4)) AS V;"), "value-returned-through-an-approximate-return-type", OwnSetup: true),
        new(new("default-value-fits-the-approximate-parameter", "CREATE PROCEDURE dbo.P @v FLOAT = 2147483647 AS BEGIN SELECT CAST(@v AS DECIMAL(38,4)) AS V; END\nGO\n", "EXEC dbo.P;"), "default-value-wider-than-the-approximate-parameter", OwnSetup: true),
    ];
}

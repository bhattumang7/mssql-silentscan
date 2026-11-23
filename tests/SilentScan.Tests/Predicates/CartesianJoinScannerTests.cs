using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class CartesianJoinScannerTests
{
    private static IReadOnlyList<CartesianJoinFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return CartesianJoinScanner.Scan(result);
    }

    private const string ConfigDdl = "CREATE TABLE dbo.Config (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL); CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, Amount INT NOT NULL);";

    private static IReadOnlyList<CartesianJoinFinding> ScanWithCatalog(string statement)
    {
        var ddl = SqlScriptParser.ParseText("ddl.sql", ConfigDdl);
        var result = SqlScriptParser.ParseText("test.sql", statement);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return CartesianJoinScanner.Scan(result, CatalogBuilder.Build([ddl]));
    }

    private static IReadOnlyList<CartesianJoinFinding> ScanWithDdl(string ddl, string statement)
    {
        var ddlResult = SqlScriptParser.ParseText("ddl.sql", ddl);
        var result = SqlScriptParser.ParseText("test.sql", statement);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return CartesianJoinScanner.Scan(result, CatalogBuilder.Build([ddlResult]));
    }

    [Theory]
    [InlineData("INT", "INT", "a.X = 1 AND b.Y = '01'")]
    [InlineData("VARCHAR(5)", "VARCHAR(5)", "a.X = 'a' AND b.Y = 'a '")]
    [InlineData("VARCHAR(5) COLLATE Latin1_General_CI_AS", "VARCHAR(5) COLLATE Latin1_General_CI_AS", "a.X = 'a' AND b.Y = 'A'")]
    [InlineData("INT", "DECIMAL(5,1)", "a.X = 1 AND b.Y = 1.0")]
    public void JoinedColumnsPinnedToDifferentLiteralsTheEngineTreatsAsEqual_NeverFires(string firstType, string secondType, string where)
    {
        var findings = ScanWithDdl(
            $"CREATE TABLE dbo.JA (X {firstType} NOT NULL); CREATE TABLE dbo.JB (Y {secondType} NOT NULL);",
            $"SELECT * FROM dbo.JA a INNER JOIN dbo.JB b ON a.X = b.Y WHERE {where};");

        Assert.DoesNotContain(findings, f => f.Kind == CartesianJoinKind.JoinPredicateEmptyWithWhereClause);
    }

    [Theory]
    [InlineData("INT", "INT", "a.X = 1 AND b.Y = 2")]
    public void JoinedColumnsPinnedToDisjointLiterals_Fires(string firstType, string secondType, string where)
    {
        var findings = ScanWithDdl(
            $"CREATE TABLE dbo.JA (X {firstType} NOT NULL); CREATE TABLE dbo.JB (Y {secondType} NOT NULL);",
            $"SELECT * FROM dbo.JA a INNER JOIN dbo.JB b ON a.X = b.Y WHERE {where};");

        Assert.Contains(findings, f => f.Kind == CartesianJoinKind.JoinPredicateEmptyWithWhereClause);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id = 1;")]
    [InlineData("DECLARE @id INT = 1; SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id = @id;")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE (c.Id = 1);")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id IN (1);")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT Val FROM dbo.Config ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) x;")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT Val FROM dbo.Config WHERE Id = 1) x;")]
    [InlineData("SELECT * FROM dbo.Orders o, dbo.Config c WHERE c.Id = 1 AND c.Val > 0;")]
    [InlineData("SELECT * FROM dbo.Config c CROSS JOIN dbo.Orders o WHERE c.Id = 1;")]
    [InlineData("SELECT * FROM dbo.Orders o, dbo.Config c WHERE c.Id = 1 AND o.Amount > 5;")]
    public void OneSideKeyPinnedToASingleRow_NeverFires(string statement)
    {
        Assert.Empty(ScanWithCatalog(statement));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders o, (SELECT MAX(Val) AS m FROM dbo.Config) x;")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT COUNT(*) AS c FROM dbo.Config) x;")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT 1 AS a) x;")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (VALUES (1)) x(a);")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT TOP (1) Val FROM dbo.Config ORDER BY Id) x;")]
    [InlineData("SELECT * FROM dbo.Orders o CROSS JOIN (SELECT TOP 1 Val FROM dbo.Config) x;")]
    public void OneSideIsAProvablyOneRowSource_NeverFires(string statement)
    {
        Assert.Empty(ScanWithCatalog(statement));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o;")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Val = 1;")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id = 1 OR c.Id = 2;")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id IN (1, 2);")]
    [InlineData("SELECT * FROM dbo.Config c, dbo.Orders o WHERE c.Id NOT IN (1);")]
    [InlineData("SELECT * FROM dbo.Config c CROSS JOIN dbo.Orders o WHERE c.Id > 1;")]
    public void NeitherSidePinnedToASingleRow_StillFires(string statement)
    {
        Assert.Single(ScanWithCatalog(statement));
    }

    [Fact]
    public void CommaJoin_NoConnectingPredicate_Fires()
    {
        var findings = Scan("SELECT * FROM dbo.A, dbo.B;");

        var finding = Assert.Single(findings);
        Assert.Equal(CartesianJoinKind.CommaJoin, finding.Kind);
    }

    [Fact]
    public void CommaJoin_WithConnectingWherePredicate_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A a, dbo.B b WHERE a.Id = b.AId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ExplicitCrossJoin_NoConnectingPredicate_Fires()
    {
        var findings = Scan("SELECT * FROM dbo.A CROSS JOIN dbo.B;");

        var finding = Assert.Single(findings);
        Assert.Equal(CartesianJoinKind.ExplicitCrossJoin, finding.Kind);
    }

    [Fact]
    public void InnerJoinOnClause_ProvidesConnectingPredicate_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.Id = b.AId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void SingleTableFrom_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UnqualifiedColumnReferenceInWhere_Declines()
    {

        var findings = Scan("SELECT * FROM dbo.A, dbo.B WHERE SomeFlag = 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ConnectingPredicateInArithmeticExpression_StillRecognizedAsConnecting()
    {
        var findings = Scan("SELECT * FROM dbo.A a, dbo.B b WHERE a.X + b.Y = 5;");

        Assert.Empty(findings);
    }

    [Fact]
    public void FiveTableCommaJoin_AllTransitivelyConnectedThroughAThirdTable_NeverFires()
    {

        var findings = Scan("""
            SELECT *
            FROM dbo.A a, dbo.B b, dbo.C c, dbo.D d, dbo.E e
            WHERE a.TypeId = b.Id
                AND b.Type = 'X'
                AND c.Id = @TripId
                AND c.OriginId = d.AddressId
                AND c.DestinationId = e.AddressId
                AND a.AgencyId = c.AgencyId;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void CommaJoin_OneGenuinelyDisconnectedTableAmongConnectedOthers_FiresOnceNotPerPair()
    {
        var findings = Scan("""
            SELECT *
            FROM dbo.A a, dbo.B b, dbo.C c
            WHERE a.Id = b.AId;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(CartesianJoinKind.CommaJoin, finding.Kind);
    }

    [Fact]
    public void OnClauseOfAThirdJoinConnectsTwoOtherwiseUnrelatedTables_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A a, dbo.B b INNER JOIN dbo.C c ON b.Id = c.BId AND a.Id = c.AId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void CountStarWildcardInsideNestedSubquery_NeverCrashes()
    {

        var findings = Scan("SELECT * FROM dbo.A, dbo.B WHERE (SELECT COUNT(*) FROM dbo.C) > 0;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NestedJoinOnOneSide_Declines()
    {

        var findings = Scan("SELECT * FROM (dbo.A a INNER JOIN dbo.B b ON a.Id = b.AId), dbo.C c;");

        Assert.Empty(findings);
    }

    [Fact]
    public void CrossApply_IsNotTreatedAsExplicitCrossJoin()
    {
        var findings = Scan("SELECT * FROM dbo.A a CROSS APPLY dbo.B b;");

        Assert.DoesNotContain(findings, f => f.Kind == CartesianJoinKind.ExplicitCrossJoin);
    }

    [Fact]
    public void ParenthesizedConnectingPredicate_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A a, dbo.B b WHERE (a.Id = b.AId);");

        Assert.Empty(findings);
    }

    [Fact]
    public void NegatedConnectingPredicate_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.A a, dbo.B b WHERE NOT (a.Id <> b.AId);");

        Assert.Empty(findings);
    }

    [Fact]
    public void ThreeWayExplicitInnerJoinChain_AllConnected_NeverFires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a JOIN dbo.B b ON a.Id = b.AId JOIN dbo.C c ON b.AId = c.Id;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NestedParenthesizedCrossJoinsOnBothSides_FiresForEachDisconnectedPair()
    {
        var findings = Scan(
            "SELECT * FROM (dbo.A CROSS JOIN dbo.B) CROSS JOIN (dbo.C CROSS JOIN dbo.D);");

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(CartesianJoinKind.ExplicitCrossJoin, f.Kind));
    }

    [Fact]
    public void QualifiedJoinWithNestedCrossJoinOnOneSide_FiresForTheNestedCrossJoin()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a JOIN (dbo.B b CROSS JOIN dbo.C c) ON a.Id = b.AId;");

        Assert.Contains(findings, f => f.Kind == CartesianJoinKind.ExplicitCrossJoin);
    }

    [Fact]
    public void TopLevelParenthesizedQualifiedJoin_NeverCrashes()
    {
        var findings = Scan(
            "SELECT * FROM (dbo.A a JOIN dbo.B b ON a.Id = b.AId);");

        Assert.Empty(findings);
    }

    [Fact]
    public void QualifiedJoinWithNestedCrossJoinOnFirstSide_FiresForTheNestedCrossJoin()
    {
        var findings = Scan(
            "SELECT * FROM (dbo.A a CROSS JOIN dbo.B b) JOIN dbo.C c ON a.Id = c.Id;");

        Assert.Contains(findings, f => f.Kind == CartesianJoinKind.ExplicitCrossJoin);
    }

    [Fact]
    public void EquiJoin_WhereClauseConstrainsBothSidesToDisjointConstants_Fires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X = 5 AND b.Y = 10;");

        var finding = Assert.Single(findings);
        Assert.Equal(CartesianJoinKind.JoinPredicateEmptyWithWhereClause, finding.Kind);
    }

    [Fact]
    public void EquiJoin_WhereClauseConstrainsBothSidesToDisjointRanges_Fires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X > 100 AND b.Y < 10;");

        Assert.Contains(findings, f => f.Kind == CartesianJoinKind.JoinPredicateEmptyWithWhereClause);
    }

    [Fact]
    public void EquiJoin_WhereClauseConstrainsBothSidesToSameConstant_NegativeControl_DoesNotFire()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X = 5 AND b.Y = 5;");

        Assert.Empty(findings);
    }

    [Fact]
    public void EquiJoin_WhereClauseConstrainsOnlyOneSide_NegativeControl_DoesNotFire()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X = 5;");

        Assert.Empty(findings);
    }

    [Fact]
    public void EquiJoin_NoWhereClause_NegativeControl_DoesNotFire()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonEquiJoin_NoColumnToColumnEquality_NegativeControl_DoesNotFire()
    {
        var findings = Scan(
            "SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.Id = b.AId WHERE a.X = 5 AND b.X = 10;");

        Assert.Empty(findings);
    }
}

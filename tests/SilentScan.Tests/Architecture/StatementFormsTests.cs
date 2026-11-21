using SilentScan.Tests.Support;

namespace SilentScan.Tests.Architecture;

public sealed class StatementFormsTests
{
    private static readonly string[] FormsNamedInTestShape =
    [
        "procedure",
        "view",
        "common table expression",
        "derived table",
        "aliases and bracketed names",
        "dynamic SQL",
    ];

    private static readonly OracleCase Query = new("q", string.Empty, "SELECT Id FROM dbo.Parent WHERE Id > 1;");

    [Fact]
    public void EveryFormNamedInTheTestShapeDocumentExists()
    {
        var present = StatementForms.All.Select(f => f.Name).ToList();

        Assert.Empty(FormsNamedInTestShape.Except(present, StringComparer.Ordinal));
    }

    [Fact]
    public void BracketedNamesFormQuotesTheSchemaAndNamesAnAliasForAQuery()
    {
        var form = StatementForms.All.Single(f => f.Name == StatementForms.BracketedNames);

        Assert.Equal("SELECT Id FROM [dbo].[Parent] AS [Aliased] WHERE Id > 1;", form.Apply(Query).Statement);
    }

    [Fact]
    public void BracketedNamesFormQuotesTheSchemaWithoutAnAliasForDataChanges()
    {
        var form = StatementForms.All.Single(f => f.Name == StatementForms.BracketedNames);

        var shaped = form.Apply(new OracleCase("d", string.Empty, "DELETE FROM dbo.Parent WHERE Id > 1;"));

        Assert.Equal("DELETE FROM [dbo].[Parent] WHERE Id > 1;", shaped.Statement);
    }

    [Fact]
    public void DynamicSqlFormDoublesQuotesInsideTheString()
    {
        var form = StatementForms.All.Single(f => f.Name == StatementForms.DynamicSql);

        var shaped = form.Apply(new OracleCase("d", string.Empty, "SELECT 'a';"));

        Assert.Equal("EXEC sp_executesql N'SELECT ''a'';';", shaped.Statement);
    }

    [Fact]
    public void QueryOnlyFormsDoNotApplyToDataChanges()
    {
        var update = "UPDATE dbo.Parent SET Id = 1;";

        var applicable = StatementForms.All.Where(f => f.Applies(update)).Select(f => f.Name).ToList();

        Assert.DoesNotContain(StatementForms.View, applicable);
        Assert.DoesNotContain(StatementForms.CommonTableExpression, applicable);
        Assert.DoesNotContain(StatementForms.DerivedTable, applicable);
        Assert.Contains(StatementForms.Procedure, applicable);
    }
}

public sealed class RuleOracleTargetsTests
{
    [Fact]
    public void NoRuleOracleNarrowsTheEngineMatrix()
    {
        var offenders = typeof(RuleOracleTargetsTests).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && typeof(RuleOracle).IsAssignableFrom(t))
            .Where(t => t.GetProperty("Targets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.DeclaringType == t)
            .Where(t => !string.Equals(t.Namespace, typeof(RuleOracleTargetsTests).Namespace, StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}

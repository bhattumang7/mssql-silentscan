using SilentScan.Core.Parsing;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class MetamorphicMutatorTests
{
    private const string Sql = """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(20) NOT NULL);
        SELECT OrderId FROM dbo.Orders WHERE Status <> 'Closed';
        """;

    [Fact]
    public void Mutate_ProducesEveryMutationCategory()
    {
        var mutations = MetamorphicMutator.Mutate(Sql);

        Assert.Contains(mutations, m => m.Name == "identifier-case-flip");
        Assert.Contains(mutations, m => m.Name == "identifier-bracket-quoting");
        Assert.Contains(mutations, m => m.Name == "comment-injection");
        Assert.Contains(mutations, m => m.Name == "blank-line-injection");
    }

    [Fact]
    public void Mutate_EveryMutationStillParsesCleanly()
    {
        var mutations = MetamorphicMutator.Mutate(Sql);

        Assert.NotEmpty(mutations);
        foreach (var mutation in mutations)
        {
            var parseResult = SqlScriptParser.ParseText("mutation-check", mutation.MutatedSql);
            Assert.False(parseResult.HasErrors, $"{mutation.Name} produced unparseable SQL: {string.Join("; ", parseResult.Errors.Select(e => e.Message))}");
        }
    }

    [Fact]
    public void Mutate_IdentifierCaseFlip_LeavesStringLiteralsUntouched()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "identifier-case-flip");

        Assert.Contains("'Closed'", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_BracketQuoting_WrapsPlainIdentifiersOnly()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "identifier-bracket-quoting");

        Assert.Contains("[Orders]", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("[Status]", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_BracketQuoting_LeavesBuiltInTypeNamesUnquoted()
    {
        var sql = """
            CREATE TABLE dbo.Products (SkuCode VARCHAR(MAX) NOT NULL);
            SELECT SkuCode FROM dbo.Products;
            """;
        var mutation = Assert.Single(MetamorphicMutator.Mutate(sql), m => m.Name == "identifier-bracket-quoting");

        Assert.Contains("VARCHAR(MAX)", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("[Products]", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_UnparseableInput_ReturnsNoMutations()
    {
        var mutations = MetamorphicMutator.Mutate("SELECT FROM WHERE (((");

        Assert.Empty(mutations);
    }
}

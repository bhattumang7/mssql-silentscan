using SilentScan.Core.Parsing;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class MetamorphicMutatorCorpusTests
{
    [Fact]
    public void Mutate_EveryPublishedExample_EveryMutationStillParsesCleanly()
    {
        var cases = RuleExampleCorpus.Build()
            .Where(c => c.Variant == RuleExampleVariant.Noncompliant && c.IsSelfContained);

        var failures = new List<string>();
        foreach (var c in cases)
        {
            foreach (var mutation in MetamorphicMutator.Mutate(c.DeployableSql))
            {
                var parseResult = SqlScriptParser.ParseText("mutation-corpus-check", mutation.MutatedSql);
                if (parseResult.HasErrors)
                {
                    failures.Add($"{c.RuleId} #{c.ExampleIndex} x {mutation.Name}: {string.Join("; ", parseResult.Errors.Select(e => e.Message))}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }
}

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
        Assert.Contains(mutations, m => m.Name == "schema-qualification-remove");
        Assert.Contains(mutations, m => m.Name == "redundant-parentheses");
        Assert.Contains(mutations, m => m.Name == "operand-order-swap");
        Assert.Contains(mutations, m => m.Name == "derived-table-wrap");
        Assert.Contains(mutations, m => m.Name == "unrelated-join");
    }

    [Fact]
    public void Mutate_SchemaQualificationAdd_PrefixesUnqualifiedTableName()
    {
        var sql = """
            CREATE TABLE Orders (OrderId INT NOT NULL PRIMARY KEY);
            SELECT OrderId FROM Orders;
            """;
        var mutation = Assert.Single(MetamorphicMutator.Mutate(sql), m => m.Name == "schema-qualification-add");

        Assert.Contains("dbo.Orders", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_SchemaQualificationRemove_StripsDboPrefix()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "schema-qualification-remove");

        Assert.DoesNotContain("dbo.Orders", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("FROM Orders", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_RedundantParentheses_WrapsWhereSearchCondition()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "redundant-parentheses");

        Assert.Contains("WHERE (Status <> 'Closed')", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_OperandOrderSwap_SwapsSidesOfEqualityComparison()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "operand-order-swap");

        Assert.Contains("'Closed' <> Status", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_DerivedTableWrap_WrapsTopLevelSelect()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "derived-table-wrap");

        Assert.Contains("SELECT * FROM (SELECT OrderId FROM dbo.Orders WHERE Status <> 'Closed') AS MetamorphicWrap", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_UnrelatedJoin_AppendsCrossJoinAfterFromClause()
    {
        var mutation = Assert.Single(MetamorphicMutator.Mutate(Sql), m => m.Name == "unrelated-join");

        Assert.Contains("FROM dbo.Orders CROSS JOIN (SELECT 1 AS MetamorphicJoinCol) AS MetamorphicJoin WHERE", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_AliasRename_RenamesAliasAndItsQualifiedColumnReferences()
    {
        var sql = """
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, Status VARCHAR(20) NOT NULL);
            SELECT o.OrderId FROM dbo.Orders AS o WHERE o.Status <> 'Closed';
            """;
        var mutation = Assert.Single(MetamorphicMutator.Mutate(sql), m => m.Name == "alias-rename");

        Assert.Contains("AS oMm", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("SELECT oMm.OrderId", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("WHERE oMm.Status <> 'Closed'", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("o.OrderId", mutation.MutatedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_AliasRename_SkipsWhenNoTableAliasPresent()
    {
        Assert.DoesNotContain(MetamorphicMutator.Mutate(Sql), m => m.Name == "alias-rename");
    }

    [Fact]
    public void Mutate_AliasRename_RenamesBareDeleteTargetAliasToo()
    {
        var sql = """
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
            DELETE o
            FROM dbo.Orders AS o
            WHERE o.OrderId NOT IN (SELECT TOP (1) o2.OrderId FROM dbo.Orders AS o2 WHERE o2.CustomerId = o.CustomerId);
            """;
        var mutation = Assert.Single(MetamorphicMutator.Mutate(sql), m => m.Name == "alias-rename");

        Assert.Contains("DELETE oMm", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("AS oMm", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.Contains("AS o2Mm", mutation.MutatedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE o\n", mutation.MutatedSql, StringComparison.Ordinal);
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

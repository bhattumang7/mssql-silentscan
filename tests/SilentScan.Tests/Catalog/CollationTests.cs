using SilentScan.Core.Catalog;

namespace SilentScan.Tests.Catalog;

public sealed class CollationTests
{
    [Theory]
    [InlineData("SQL_Latin1_General_CP1_CI_AS")]
    [InlineData("sql_latin1_general_cp1_ci_as")]
    public void IsSqlFamily_SqlPrefixedCollation_ReturnsTrue(string name)
    {
        var collation = new Collation(name);

        Assert.True(collation.IsSqlFamily);
        Assert.False(collation.IsWindowsFamily);
    }

    [Fact]
    public void IsSqlFamily_WindowsCollation_ReturnsFalse()
    {
        var collation = new Collation("Latin1_General_CI_AS");

        Assert.False(collation.IsSqlFamily);
        Assert.True(collation.IsWindowsFamily);
    }
}

using SilentScan.Verify;

namespace SilentScan.Tests.Support;

public sealed class SqlServerOptionsTests
{
    [Fact]
    public void BuildConnectionString_WithUserId_UsesSqlLogin()
    {
        var options = new SqlServerOptions("localhost", 1433, "sa", "secret");

        var connectionString = options.BuildConnectionString("db");

        Assert.Contains("User Id=sa;", connectionString);
        Assert.Contains("Password=secret;", connectionString);
        Assert.DoesNotContain("Integrated Security", connectionString);
        Assert.Contains("Database=db;", connectionString);
    }

    [Fact]
    public void BuildConnectionString_WithoutUserId_UsesWindowsLogin()
    {
        var options = SqlServerOptions.WindowsLogin("localhost", 1433);

        var connectionString = options.BuildConnectionString("db");

        Assert.Contains("Integrated Security=True;", connectionString);
        Assert.DoesNotContain("User Id", connectionString);
        Assert.DoesNotContain("Password", connectionString);
        Assert.Contains("Server=localhost,1433;", connectionString);
        Assert.Contains("Database=db;", connectionString);
    }
}

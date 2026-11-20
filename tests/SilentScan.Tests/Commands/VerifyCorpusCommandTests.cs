using SilentScan.Cli.Commands;

namespace SilentScan.Tests.Commands;

public sealed class VerifyCorpusCommandTests
{
    [Fact]
    public void TryParseTarget_IntegratedSecurityWithPort_UsesWindowsLogin()
    {
        var ok = VerifyCorpusCommand.TryParseTarget("Server=host,1444;Database=Db;Integrated Security=True;", out var options, out var database);

        Assert.True(ok);
        Assert.Equal("host", options.Host);
        Assert.Equal(1444, options.Port);
        Assert.Equal(string.Empty, options.UserId);
        Assert.Equal("Db", database);
    }

    [Fact]
    public void TryParseTarget_SqlLoginWithoutPort_DefaultsTo1433AndKeepsCredentials()
    {
        var ok = VerifyCorpusCommand.TryParseTarget("Server=host;Database=Db;User Id=u;Password=p;", out var options, out _);

        Assert.True(ok);
        Assert.Equal(1433, options.Port);
        Assert.Equal("u", options.UserId);
        Assert.Equal("p", options.Password);
    }

    [Theory]
    [InlineData("Server=host\\inst;Database=Db;Integrated Security=True;")]
    [InlineData("Server=host;Integrated Security=True;")]
    [InlineData("Server=host,abc;Database=Db;Integrated Security=True;")]
    public void TryParseTarget_UnsupportedTarget_ReturnsFalse(string connectionString)
    {
        Assert.False(VerifyCorpusCommand.TryParseTarget(connectionString, out _, out _));
    }
}

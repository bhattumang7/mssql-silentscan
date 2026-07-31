using SilentScan.Bench.Commands;
using SilentScan.Verify.Commands;

namespace SilentScan.Tests.Commands;

public sealed class RootCommandTests
{
    [Fact]
    public void VerifyRootCommand_Create_HasDescription()
    {
        var command = VerifyRootCommand.Create();

        Assert.Contains("silentscan-verify", command.Description);
    }

    [Fact]
    public void BenchRootCommand_Create_HasDescription()
    {
        var command = BenchRootCommand.Create();

        Assert.Contains("silentscan-bench", command.Description);
    }
}

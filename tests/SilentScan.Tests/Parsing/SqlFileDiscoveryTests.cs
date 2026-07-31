using SilentScan.Core.Parsing;

namespace SilentScan.Tests.Parsing;

public sealed class SqlFileDiscoveryTests
{
    [Fact]
    public void EnumerateSqlFiles_SingleFile_ReturnsThatFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "phase0_spike.sql");

        var files = SqlFileDiscovery.EnumerateSqlFiles(path);

        Assert.Equal([path], files);
    }

    [Fact]
    public void EnumerateSqlFiles_Directory_ReturnsSqlFilesInDeterministicOrder()
    {
        var tempDir = Directory.CreateTempSubdirectory("silentscan-discovery-");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "b.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(tempDir.FullName, "a.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(tempDir.FullName, "ignore.txt"), "not sql");

            var files = SqlFileDiscovery.EnumerateSqlFiles(tempDir.FullName);

            Assert.Equal(
                [Path.Combine(tempDir.FullName, "a.sql"), Path.Combine(tempDir.FullName, "b.sql")],
                files);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}

using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class ServerScopedStatementGuardTests
{
    [Fact]
    public void ContainsServerScopedDdl_LogonTriggerOnAllServer_ReturnsTrue()
    {
        const string sql = """
            CREATE TRIGGER trg_RestrictByHost
            ON ALL SERVER
            FOR LOGON
            AS
            BEGIN
                IF HOST_NAME() NOT IN ('APPSERVER01', 'APPSERVER02')
                BEGIN
                    ROLLBACK;
                END;
            END;
            """;

        Assert.True(ServerScopedStatementGuard.ContainsServerScopedDdl(sql));
    }

    [Fact]
    public void ContainsServerScopedDdl_DatabaseScopedTrigger_ReturnsFalse()
    {
        const string sql = """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);
            GO
            CREATE TRIGGER trg_OnT
            ON dbo.T
            AFTER INSERT
            AS
            BEGIN
                SELECT 1;
            END;
            """;

        Assert.False(ServerScopedStatementGuard.ContainsServerScopedDdl(sql));
    }

    [Fact]
    public void ContainsServerScopedDdl_CreateLogin_ReturnsTrue()
    {
        const string sql = "CREATE LOGIN AppLogin WITH PASSWORD = 'not-a-real-secret-Aa1!';";

        Assert.True(ServerScopedStatementGuard.ContainsServerScopedDdl(sql));
    }
}

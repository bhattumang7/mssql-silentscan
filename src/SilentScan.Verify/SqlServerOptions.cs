namespace SilentScan.Verify;

public sealed record SqlServerOptions(string Host, int Port, string UserId, string Password)
{
    private static string? ServerOverride { get; } = Environment.GetEnvironmentVariable("SILENTSCAN_SQL_SERVER") is { Length: > 0 } server ? server : null;

    public static SqlServerOptions LocalDocker { get; } = LocalInstance("SILENTSCAN_SQL_PORT", 14330);

    public static SqlServerOptions LocalDockerLatest { get; } = LocalInstance("SILENTSCAN_SQL2025_PORT", 14331);

    private static SqlServerOptions LocalInstance(string portVariable, int defaultPort)
    {
        if (ServerOverride is not null)
        {
            return WindowsLogin(ServerOverride, 0);
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable(portVariable), out var parsed) ? parsed : defaultPort;
        return new SqlServerOptions("localhost", port, "sa", Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");
    }

    public static SqlServerOptions WindowsLogin(string host, int port) => new(host, port, string.Empty, string.Empty);

    public string BuildConnectionString(string? database = null) =>
        $"Server={(Port > 0 ? $"{Host},{Port}" : Host)};"
        + (string.IsNullOrEmpty(UserId) ? "Integrated Security=True;" : $"User Id={UserId};Password={Password};")
        + $"{(database is null ? string.Empty : $"Database={database};")}"
        + "TrustServerCertificate=True;";
}

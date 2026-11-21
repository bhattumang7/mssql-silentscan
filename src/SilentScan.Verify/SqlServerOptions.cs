namespace SilentScan.Verify;

public sealed record SqlServerOptions(string Host, int Port, string UserId, string Password)
{
    private static string? ServerOverride { get; } = Environment.GetEnvironmentVariable("SILENTSCAN_SQL_SERVER") is { Length: > 0 } server ? server : null;

    public static SqlServerOptions LocalDocker { get; } = ServerOverride is not null ? WindowsLogin(ServerOverride, 0) : new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL_PORT"), out var port) ? port : 14330,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    public static SqlServerOptions LocalDockerLatest { get; } = ServerOverride is not null ? WindowsLogin(ServerOverride, 0) : new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var latestPort) ? latestPort : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    public static SqlServerOptions WindowsLogin(string host, int port) => new(host, port, string.Empty, string.Empty);

    public string BuildConnectionString(string? database = null) =>
        $"Server={(Port > 0 ? $"{Host},{Port}" : Host)};"
        + (string.IsNullOrEmpty(UserId) ? "Integrated Security=True;" : $"User Id={UserId};Password={Password};")
        + $"{(database is null ? string.Empty : $"Database={database};")}"
        + "TrustServerCertificate=True;";
}

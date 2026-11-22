using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Verify.Catalog;

namespace SilentScan.Live.Catalog;

public sealed class DatabaseConfigurationReader
{
    private readonly string _connectionString;

    public DatabaseConfigurationReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseConfigurationFinding>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var snapshot = await ReadSnapshotAsync(connection, cancellationToken);
        if (snapshot is null)
        {
            return [];
        }

        var findings = new List<DatabaseConfigurationFinding>();
        AddSnapshotFindings(findings, snapshot);

        var engineDefaultCompatibilityLevel = await ReadEngineDefaultCompatibilityLevelAsync(connection, cancellationToken);

        if (engineDefaultCompatibilityLevel > snapshot.CompatibilityLevel)
        {
            await AddSpatialCompatibilityFindingsAsync(
                connection, snapshot.DatabaseName, engineDefaultCompatibilityLevel.Value, findings, cancellationToken);
        }


        return findings;
    }

    private static async Task<DatabaseConfigurationSnapshot?> ReadSnapshotAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateReadOnlyCommand(
            """
            SELECT name, is_auto_shrink_on, is_auto_close_on,
                   is_auto_create_stats_on, is_auto_update_stats_on,
                   compatibility_level
            FROM sys.databases
            WHERE database_id = DB_ID();
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DatabaseConfigurationSnapshot(
            DatabaseName: reader.GetString(0),
            IsAutoShrinkOn: reader.GetBoolean(1),
            IsAutoCloseOn: reader.GetBoolean(2),
            IsAutoCreateStatsOn: reader.GetBoolean(3),
            IsAutoUpdateStatsOn: reader.GetBoolean(4),
            CompatibilityLevel: reader.GetByte(5));
    }

    private static void AddSnapshotFindings(List<DatabaseConfigurationFinding> findings, DatabaseConfigurationSnapshot snapshot)
    {
        if (snapshot.IsAutoShrinkOn)
        {
            findings.Add(new DatabaseConfigurationFinding(DatabaseConfigurationFindingKind.AutoShrinkOn, snapshot.DatabaseName));
        }

        if (snapshot.IsAutoCloseOn)
        {
            findings.Add(new DatabaseConfigurationFinding(DatabaseConfigurationFindingKind.AutoCloseOn, snapshot.DatabaseName));
        }

        if (!snapshot.IsAutoCreateStatsOn)
        {
            findings.Add(new DatabaseConfigurationFinding(DatabaseConfigurationFindingKind.AutoCreateStatisticsOff, snapshot.DatabaseName));
        }

        if (!snapshot.IsAutoUpdateStatsOn)
        {
            findings.Add(new DatabaseConfigurationFinding(DatabaseConfigurationFindingKind.AutoUpdateStatisticsOff, snapshot.DatabaseName));
        }
    }

    private static async Task<int?> ReadEngineDefaultCompatibilityLevelAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var modelCommand = connection.CreateReadOnlyCommand(
            "SELECT compatibility_level FROM sys.databases WHERE name = 'model';");
        await using var modelReader = await modelCommand.ExecuteReaderAsync(cancellationToken);

        if (!await modelReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (int)modelReader.GetByte(0);
    }

    private static async Task AddSpatialCompatibilityFindingsAsync(
        SqlConnection connection,
        string databaseName,
        int engineDefaultCompatibilityLevel,
        List<DatabaseConfigurationFinding> findings,
        CancellationToken cancellationToken)
    {
        await using var compatibilityCommand = connection.CreateReadOnlyCommand(
            $"""
            SELECT QUOTENAME(OBJECT_SCHEMA_NAME(change_object.major_id)) + N'.' + QUOTENAME(OBJECT_NAME(change_object.major_id)) + COALESCE(N'.' + QUOTENAME(index_row.name), N''), change_object.dependency
            FROM sys.dm_db_objects_disabled_on_compatibility_level_change({engineDefaultCompatibilityLevel}) AS change_object
            LEFT JOIN sys.indexes AS index_row
                ON index_row.object_id = change_object.major_id
               AND index_row.index_id = change_object.minor_id
            WHERE change_object.class_desc = N'INDEX'
              AND (change_object.dependency LIKE N'geography::%' OR change_object.dependency LIKE N'geometry::%')
            ORDER BY change_object.major_id, change_object.minor_id;
            """);

        try
        {
            await using var compatibilityReader = await compatibilityCommand.ExecuteReaderAsync(cancellationToken);
            while (await compatibilityReader.ReadAsync(cancellationToken))
            {
                findings.Add(new DatabaseConfigurationFinding(
                    DatabaseConfigurationFindingKind.SpatialPersistedComputedColumnDisabledOnCompatibilityLevelChange,
                    databaseName,
                    AffectedObjectName: compatibilityReader.GetString(0),
                    Dependency: compatibilityReader.GetString(1),
                    TargetCompatibilityLevel: engineDefaultCompatibilityLevel));
            }
        }
        catch (SqlException ex)
        {
            _ = ex;
        }
    }

    private sealed record DatabaseConfigurationSnapshot(
        string DatabaseName,
        bool IsAutoShrinkOn,
        bool IsAutoCloseOn,
        bool IsAutoCreateStatsOn,
        bool IsAutoUpdateStatsOn,
        int CompatibilityLevel);

    public async Task<bool> ReadIsParameterizationForcedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateReadOnlyCommand(
            "SELECT is_parameterization_forced FROM sys.databases WHERE database_id = DB_ID();");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) && reader.GetBoolean(0);
    }
}

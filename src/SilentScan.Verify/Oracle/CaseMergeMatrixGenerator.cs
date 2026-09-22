using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Catalog;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed record CaseMergeTypeSpec(SqlTypeCategory Category, string Syntax, SqlType Declared);

public sealed record CaseMergeMismatch(
    string LeftSyntax,
    string RightSyntax,
    SqlType? Predicted,
    SqlType? Actual);

public static class CaseMergeSpecs
{
    public static IReadOnlyList<CaseMergeTypeSpec> DecimalSpecs { get; } =
    [
        new(SqlTypeCategory.Decimal, "DECIMAL(5,2)", new SqlType(SqlTypeCategory.Decimal, Precision: 5, Scale: 2)),
        new(SqlTypeCategory.Decimal, "DECIMAL(10,4)", new SqlType(SqlTypeCategory.Decimal, Precision: 10, Scale: 4)),
        new(SqlTypeCategory.Decimal, "DECIMAL(18,0)", new SqlType(SqlTypeCategory.Decimal, Precision: 18, Scale: 0)),
        new(SqlTypeCategory.Decimal, "DECIMAL(38,10)", new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 10)),
        new(SqlTypeCategory.Decimal, "DECIMAL(4,4)", new SqlType(SqlTypeCategory.Decimal, Precision: 4, Scale: 4)),
    ];

    public static IReadOnlyList<CaseMergeTypeSpec> StringSpecs { get; } =
    [
        new(SqlTypeCategory.VarChar, "VARCHAR(10)", new SqlType(SqlTypeCategory.VarChar, Length: 10)),
        new(SqlTypeCategory.VarChar, "VARCHAR(50)", new SqlType(SqlTypeCategory.VarChar, Length: 50)),
        new(SqlTypeCategory.NVarChar, "NVARCHAR(10)", new SqlType(SqlTypeCategory.NVarChar, Length: 10)),
        new(SqlTypeCategory.NVarChar, "NVARCHAR(50)", new SqlType(SqlTypeCategory.NVarChar, Length: 50)),
        new(SqlTypeCategory.Char, "CHAR(10)", new SqlType(SqlTypeCategory.Char, Length: 10)),
        new(SqlTypeCategory.Char, "CHAR(30)", new SqlType(SqlTypeCategory.Char, Length: 30)),
        new(SqlTypeCategory.NChar, "NCHAR(20)", new SqlType(SqlTypeCategory.NChar, Length: 20)),
    ];

    public static IReadOnlyList<CaseMergeTypeSpec> ExactNumericSpecs { get; } =
    [
        new(SqlTypeCategory.TinyInt, "TINYINT", new SqlType(SqlTypeCategory.TinyInt)),
        new(SqlTypeCategory.SmallInt, "SMALLINT", new SqlType(SqlTypeCategory.SmallInt)),
        new(SqlTypeCategory.Int, "INT", new SqlType(SqlTypeCategory.Int)),
        new(SqlTypeCategory.BigInt, "BIGINT", new SqlType(SqlTypeCategory.BigInt)),
        new(SqlTypeCategory.SmallMoney, "SMALLMONEY", new SqlType(SqlTypeCategory.SmallMoney)),
        new(SqlTypeCategory.Money, "MONEY", new SqlType(SqlTypeCategory.Money)),
        new(SqlTypeCategory.Decimal, "DECIMAL(5,2)", new SqlType(SqlTypeCategory.Decimal, Precision: 5, Scale: 2)),
        new(SqlTypeCategory.Decimal, "DECIMAL(10,4)", new SqlType(SqlTypeCategory.Decimal, Precision: 10, Scale: 4)),
        new(SqlTypeCategory.Decimal, "DECIMAL(18,0)", new SqlType(SqlTypeCategory.Decimal, Precision: 18, Scale: 0)),
        new(SqlTypeCategory.Decimal, "DECIMAL(28,12)", new SqlType(SqlTypeCategory.Decimal, Precision: 28, Scale: 12)),
        new(SqlTypeCategory.Decimal, "DECIMAL(38,4)", new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 4)),
        new(SqlTypeCategory.Decimal, "DECIMAL(38,37)", new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 37)),
    ];

    public static IReadOnlyList<CaseMergeTypeSpec> DateTimeSpecs { get; } =
    [
        new(SqlTypeCategory.Date, "DATE", new SqlType(SqlTypeCategory.Date)),
        new(SqlTypeCategory.Time, "TIME(3)", new SqlType(SqlTypeCategory.Time, Scale: 3)),
        new(SqlTypeCategory.Time, "TIME(7)", new SqlType(SqlTypeCategory.Time, Scale: 7)),
        new(SqlTypeCategory.SmallDateTime, "SMALLDATETIME", new SqlType(SqlTypeCategory.SmallDateTime)),
        new(SqlTypeCategory.DateTime, "DATETIME", new SqlType(SqlTypeCategory.DateTime)),
        new(SqlTypeCategory.DateTime2, "DATETIME2(0)", new SqlType(SqlTypeCategory.DateTime2, Scale: 0)),
        new(SqlTypeCategory.DateTime2, "DATETIME2(7)", new SqlType(SqlTypeCategory.DateTime2, Scale: 7)),
        new(SqlTypeCategory.DateTimeOffset, "DATETIMEOFFSET(3)", new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 3)),
        new(SqlTypeCategory.DateTimeOffset, "DATETIMEOFFSET(7)", new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7)),
    ];

    public static IReadOnlyList<CaseMergeTypeSpec> BinarySpecs { get; } =
    [
        new(SqlTypeCategory.Binary, "BINARY(8)", new SqlType(SqlTypeCategory.Binary, Length: 8)),
        new(SqlTypeCategory.Binary, "BINARY(20)", new SqlType(SqlTypeCategory.Binary, Length: 20)),
        new(SqlTypeCategory.VarBinary, "VARBINARY(8)", new SqlType(SqlTypeCategory.VarBinary, Length: 8)),
        new(SqlTypeCategory.VarBinary, "VARBINARY(20)", new SqlType(SqlTypeCategory.VarBinary, Length: 20)),
    ];

    public static IReadOnlyList<CaseMergeTypeSpec> AllTypeSpecs { get; } =
    [
        new(SqlTypeCategory.Bit, "BIT", new SqlType(SqlTypeCategory.Bit)),
        new(SqlTypeCategory.TinyInt, "TINYINT", new SqlType(SqlTypeCategory.TinyInt)),
        new(SqlTypeCategory.SmallInt, "SMALLINT", new SqlType(SqlTypeCategory.SmallInt)),
        new(SqlTypeCategory.Int, "INT", new SqlType(SqlTypeCategory.Int)),
        new(SqlTypeCategory.BigInt, "BIGINT", new SqlType(SqlTypeCategory.BigInt)),
        new(SqlTypeCategory.SmallMoney, "SMALLMONEY", new SqlType(SqlTypeCategory.SmallMoney)),
        new(SqlTypeCategory.Money, "MONEY", new SqlType(SqlTypeCategory.Money)),
        new(SqlTypeCategory.Decimal, "DECIMAL(10,4)", new SqlType(SqlTypeCategory.Decimal, Precision: 10, Scale: 4)),
        new(SqlTypeCategory.Real, "REAL", new SqlType(SqlTypeCategory.Real)),
        new(SqlTypeCategory.Float, "FLOAT", new SqlType(SqlTypeCategory.Float)),
        new(SqlTypeCategory.UniqueIdentifier, "UNIQUEIDENTIFIER", new SqlType(SqlTypeCategory.UniqueIdentifier)),
        new(SqlTypeCategory.Char, "CHAR(10)", new SqlType(SqlTypeCategory.Char, Length: 10)),
        new(SqlTypeCategory.VarChar, "VARCHAR(10)", new SqlType(SqlTypeCategory.VarChar, Length: 10)),
        new(SqlTypeCategory.VarChar, "VARCHAR(MAX)", new SqlType(SqlTypeCategory.VarChar, IsMax: true)),
        new(SqlTypeCategory.NChar, "NCHAR(10)", new SqlType(SqlTypeCategory.NChar, Length: 10)),
        new(SqlTypeCategory.NVarChar, "NVARCHAR(10)", new SqlType(SqlTypeCategory.NVarChar, Length: 10)),
        new(SqlTypeCategory.NVarChar, "NVARCHAR(MAX)", new SqlType(SqlTypeCategory.NVarChar, IsMax: true)),
        new(SqlTypeCategory.Binary, "BINARY(8)", new SqlType(SqlTypeCategory.Binary, Length: 8)),
        new(SqlTypeCategory.VarBinary, "VARBINARY(8)", new SqlType(SqlTypeCategory.VarBinary, Length: 8)),
        new(SqlTypeCategory.VarBinary, "VARBINARY(MAX)", new SqlType(SqlTypeCategory.VarBinary, IsMax: true)),
        new(SqlTypeCategory.Date, "DATE", new SqlType(SqlTypeCategory.Date)),
        new(SqlTypeCategory.Time, "TIME(7)", new SqlType(SqlTypeCategory.Time, Scale: 7)),
        new(SqlTypeCategory.SmallDateTime, "SMALLDATETIME", new SqlType(SqlTypeCategory.SmallDateTime)),
        new(SqlTypeCategory.DateTime, "DATETIME", new SqlType(SqlTypeCategory.DateTime)),
        new(SqlTypeCategory.DateTime2, "DATETIME2(7)", new SqlType(SqlTypeCategory.DateTime2, Scale: 7)),
        new(SqlTypeCategory.DateTimeOffset, "DATETIMEOFFSET(7)", new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7)),
    ];
}

public sealed class CaseMergeMatrixGenerator
{
    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public CaseMergeMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<IReadOnlyList<CaseMergeMismatch>> RunAsync(
        IReadOnlyList<CaseMergeTypeSpec> specs, CancellationToken cancellationToken = default)
    {
        var mismatches = new List<CaseMergeMismatch>();
        var database = "SilentScanCaseMergeMatrix_" + Guid.NewGuid().ToString("N")[..8];

        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        try
        {
            await using var connection = new SqlConnection(_options.BuildConnectionString(database));
            await connection.OpenAsync(cancellationToken);

            var pairs = specs.SelectMany(left => specs.Select(right => (Left: left, Right: right)))
                .Where(p => p.Left != p.Right)
                .ToList();

            for (var i = 0; i < pairs.Count; i++)
            {
                var (left, right) = pairs[i];
                var tableName = $"dbo.T{i}";
                await using (var create = connection.CreateCommand())
                {
                    create.CommandText = $"CREATE TABLE {tableName} (L {left.Syntax} NULL, R {right.Syntax} NULL);";
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }

                var probe = $"SELECT CASE WHEN L IS NOT NULL THEN L ELSE R END AS Merged FROM {tableName}";
                var (compileFailed, actual) = await DescribeMergedColumnAsync(connection, probe, cancellationToken);

                if (compileFailed)
                {
                    continue;
                }

                var predicted = PredictMerge(left.Declared, right.Declared);

                if (!TypesMatch(predicted, actual))
                {
                    mismatches.Add(new CaseMergeMismatch(left.Syntax, right.Syntax, predicted, actual));
                }
            }
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }

        return mismatches;
    }

    private static async Task<(bool CompileFailed, SqlType? Type)> DescribeMergedColumnAsync(
        SqlConnection connection, string probeText, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.error_number, ty.name AS type_name, r.max_length, r.precision, r.scale, r.collation_name
            FROM sys.dm_exec_describe_first_result_set(@probeText, NULL, 0) r
            LEFT JOIN sys.types ty ON ty.user_type_id = r.system_type_id
            ORDER BY r.column_ordinal;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@probeText", probeText);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!await reader.IsDBNullAsync(0, cancellationToken))
            {
                return (CompileFailed: true, Type: null);
            }

            var type = LiveTypeMapper.BuildType(
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetByte(3),
                reader.GetByte(4),
                await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5));
            return (CompileFailed: false, Type: type);
        }

        return (CompileFailed: false, Type: null);
    }

    private static SqlType? PredictMerge(SqlType left, SqlType right)
    {
        var typesByName = new Dictionary<string, SqlType?> { ["L"] = left, ["R"] = right };
        var parser = new TSql160Parser(true);
        using var reader = new StringReader("SELECT CASE WHEN L IS NOT NULL THEN L ELSE R END;");
        var fragment = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
        {
            return null;
        }

        var script = (TSqlScript)fragment;
        var select = (SelectStatement)script.Batches[0].Statements[0];
        var spec = (QuerySpecification)select.QueryExpression;
        var expression = ((SelectScalarExpression)spec.SelectElements[0]).Expression;

        return ExpressionTypeInferencer.Resolve(
            expression,
            e => e is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { } last] }
                ? typesByName.GetValueOrDefault(last.Value)
                : null,
            typeAliases: null);
    }

    private static bool TypesMatch(SqlType? predicted, SqlType? actual)
    {
        if (predicted is null || actual is null)
        {
            return predicted is null && actual is null;
        }

        return predicted.Category == actual.Category
            && predicted.Precision == actual.Precision
            && predicted.Scale == actual.Scale
            && predicted.IsMax == actual.IsMax
            && (predicted.IsMax || predicted.Length == actual.Length);
    }
}

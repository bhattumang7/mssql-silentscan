using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class NotNullPredicateContradictionVerifier : RuleVerifier<CheckConstraintPredicateContradictionFinding>
{
    public override string RuleId => FindingRuleIds.NotNullPredicateContradictionRuleId;

    protected override Perturbation Describe(CheckConstraintPredicateContradictionFinding finding)
    {
        if (!string.Equals(finding.RuleId, RuleId, StringComparison.Ordinal))
        {
            return Perturbation.NotApplicable("The finding is not about a NOT NULL column.");
        }

        if (ColumnAddress.TryParse(finding.TableQualifiedName, finding.ColumnName) is not { } address)
        {
            return Perturbation.NotApplicable($"'{finding.TableQualifiedName}.{finding.ColumnName}' is not a plain persisted table column the probe can address.");
        }

        return new Perturbation(
            [address.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            async (connection, transaction, cancellationToken) =>
            {
                var type = await ColumnTypeText.ReadAsync(connection, transaction, address, cancellationToken);
                return type is null
                    ? 0
                    : await ColumnAddress.ExecuteAsync(connection, transaction, $"ALTER TABLE {address.Table} ALTER COLUMN {address.Column} {type} NULL;", cancellationToken);
            },
            static _ => false,
            touchesReadAfterCondition: true);
    }
}

[VerifierSupport]
internal static class ColumnTypeText
{
    public static async Task<string?> ReadAsync(
        SqlConnection connection, SqlTransaction transaction, ColumnAddress address, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT t.name, c.max_length, c.precision, c.scale FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id "
            + "WHERE c.object_id = OBJECT_ID(@table) AND c.name = @column;",
            connection,
            transaction);
        command.Parameters.AddWithValue("@table", address.Table);
        command.Parameters.AddWithValue("@column", address.Column.Trim('[', ']'));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var name = reader.GetString(0);
        var length = reader.GetInt16(1);
        var precision = reader.GetByte(2);
        var scale = reader.GetByte(3);
        var characters = string.Create(CultureInfo.InvariantCulture, $"{(length < 0 ? "max" : length.ToString(CultureInfo.InvariantCulture))}");
        return name switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "bit" or "date" or "money" or "float" or "real" or "uniqueidentifier" => name,
            "varchar" or "char" or "varbinary" or "binary" => $"{name}({characters})",
            "nvarchar" or "nchar" => $"{name}({(length < 0 ? "max" : (length / 2).ToString(CultureInfo.InvariantCulture))})",
            "decimal" or "numeric" => $"{name}({precision},{scale})",
            "datetime2" or "time" or "datetimeoffset" => $"{name}({scale})",
            _ => null,
        };
    }
}

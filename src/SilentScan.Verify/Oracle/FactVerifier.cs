using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;

namespace SilentScan.Verify.Oracle;

public abstract class FactVerifier<TFinding> : RuleVerifier<TFinding>
    where TFinding : class, IFinding
{
    protected abstract string FactSql(TFinding finding);

    protected abstract string FactDescription(TFinding finding);

    protected override async Task<EngineVerification> ObserveAsync(
        SqlConnection connection, TFinding finding, string statement, CancellationToken cancellationToken)
    {
        var description = FactDescription(finding);
        try
        {
            await using var command = new SqlCommand(FactSql(finding), connection) { CommandTimeout = 120 };
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null or DBNull)
            {
                return new EngineVerification(EngineVerdict.NotDecided, $"The engine returned no answer for: {description}.");
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1
                ? new EngineVerification(EngineVerdict.Confirmed, $"The engine confirms: {description}.")
                : new EngineVerification(EngineVerdict.Refuted, $"The engine contradicts: {description}.");
        }
        catch (SqlException ex)
        {
            return new EngineVerification(EngineVerdict.NotDecided, $"The fact query for '{description}' failed: {ex.Message}");
        }
    }

    protected static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

using System.Globalization;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TemporalBoundaryPrecisionVerifier : RuleVerifier<TemporalBoundaryPrecisionFinding>
{
    private const int NanosecondsDigits = 9;

    public override string RuleId => FindingRuleIds.TemporalBoundaryPrecisionRuleId;

    protected override Perturbation Describe(TemporalBoundaryPrecisionFinding finding)
    {
        if (ColumnAddress.TryParse(finding.TableQualifiedName, finding.ColumnName) is not { } address)
        {
            return Perturbation.NotApplicable($"'{finding.TableQualifiedName}.{finding.ColumnName}' is not a plain persisted table column the probe can address.");
        }

        var gapNanoseconds = Pow10(NanosecondsDigits - finding.BoundaryLiteralFractionalDigits) - Pow10(NanosecondsDigits - finding.ColumnScale);
        if (gapNanoseconds <= 0)
        {
            return Perturbation.NotApplicable("The boundary literal has as many fractional digits as the column, so there is no precision gap to place a value in.");
        }

        var boundary = finding.BoundaryLiteralText.Replace("'", "''", StringComparison.Ordinal);
        return new Perturbation(
            [address.Touched],
            Difference.ResultEmptied,
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(
                connection, transaction, $"UPDATE {address.Table} SET {address.Column} = '{boundary}';", cancellationToken),
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(
                connection,
                transaction,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE {address.Table} SET {address.Column} = DATEADD(NANOSECOND, {gapNanoseconds}, {address.Column});"),
                cancellationToken),
            static _ => false);
    }

    private static long Pow10(int exponent) => (long)Math.Pow(10, exponent);
}

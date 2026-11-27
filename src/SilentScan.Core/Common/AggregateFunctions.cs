namespace SilentScan.Core.Common;

internal static class AggregateFunctions
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "COUNT_BIG", "AVG", "MIN", "MAX", "PRODUCT", "APPROX_COUNT_DISTINCT",
        "APPROX_PERCENTILE_CONT", "APPROX_PERCENTILE_DISC", "CHECKSUM_AGG", "GROUPING", "GROUPING_ID",
        "STDEV", "STDEVP", "STRING_AGG", "VAR", "VARP", "JSON_ARRAYAGG", "JSON_OBJECTAGG",
    };
}

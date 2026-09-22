using System.Text.RegularExpressions;

namespace SilentScan.Tests.Support;

internal static partial class SqlBatchText
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    [GeneratedRegex("CREATE PROCEDURE")]
    private static partial Regex CreateProcedure();

    [GeneratedRegex("CREATE TRIGGER")]
    private static partial Regex CreateTrigger();

    public static string[] SplitBatches(string sql) => GoSeparator().Split(sql);

    public static int CountCreateProcedure(string sql) => CreateProcedure().Count(sql);

    public static int CountCreateTrigger(string sql) => CreateTrigger().Count(sql);
}

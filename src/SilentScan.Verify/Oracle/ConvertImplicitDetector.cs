using System.Xml.Linq;

namespace SilentScan.Verify.Oracle;

/// <summary>
/// Finds CONVERT_IMPLICIT applied to a COLUMN in a showplan XML - the oracle confirmation
/// signal for SCAN_FORCED findings (CLAUDE.md: "search ScalarOperator/Convert with
/// Implicit=\"true\" over a ColumnReference"). A Convert whose input is a ColumnReference
/// means the COLUMN converted, which is what loses the seek; a Convert over a parameter or
/// literal means the harmless side converted and is not reported here.
/// </summary>
public static class ConvertImplicitDetector
{
    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static IReadOnlyList<ConvertImplicitFinding> FindColumnConversions(string planXml)
    {
        var doc = XDocument.Parse(planXml);

        return doc.Descendants(ShowPlanNs + "Convert")
            .Where(convert => (string?)convert.Attribute("Implicit") == "1")
            .Select(convert => new
            {
                Convert = convert,
                ColumnRef = convert.Descendants(ShowPlanNs + "ColumnReference").FirstOrDefault(),
            })
            .Where(x => x.ColumnRef is not null)
            .Select(x => new ConvertImplicitFinding(
                Database: TrimBrackets((string?)x.ColumnRef!.Attribute("Database")),
                Schema: TrimBrackets((string?)x.ColumnRef.Attribute("Schema")),
                Table: TrimBrackets((string?)x.ColumnRef.Attribute("Table")),
                Column: (string?)x.ColumnRef.Attribute("Column"),
                ConvertedToDataType: (string?)x.Convert.Attribute("DataType") ?? "unknown"))
            .ToList();
    }

    private static string? TrimBrackets(string? bracketedIdentifier) =>
        bracketedIdentifier?.Trim('[', ']');
}

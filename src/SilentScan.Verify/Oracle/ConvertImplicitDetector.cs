using System.Xml.Linq;

namespace SilentScan.Verify.Oracle;

public static class ConvertImplicitDetector
{
    private const string ColumnLiteral = "Column";

    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private const string TableAttributeName = "Table";

    public static IReadOnlyList<ConvertImplicitFinding> FindColumnConversions(string planXml) =>
        FindColumnConversionsCore(planXml, includeUnattributed: false);

    public static IReadOnlyList<ConvertImplicitFinding> FindColumnConversionsIncludingUnattributed(string planXml) =>
        FindColumnConversionsCore(planXml, includeUnattributed: true);

    private static List<ConvertImplicitFinding> FindColumnConversionsCore(string planXml, bool includeUnattributed)
    {
        var doc = XDocument.Parse(planXml);

        return doc.Descendants(ShowPlanNs + "Convert")
            .Where(convert => IsImplicit((string?)convert.Attribute("Implicit")))
            .Select(convert => new
            {
                Convert = convert,

                ColumnRef = convert.Descendants(ShowPlanNs + "ColumnReference")
                    .FirstOrDefault(c => !string.IsNullOrEmpty((string?)c.Attribute(TableAttributeName)))
                    ?? (includeUnattributed
                        ? convert.Descendants(ShowPlanNs + "ColumnReference").FirstOrDefault(IsNamedColumn)
                        : null),
            })
            .Where(x => x.ColumnRef is not null)
            .Select(x => new ConvertImplicitFinding(
                Database: TrimBrackets((string?)x.ColumnRef!.Attribute("Database")),
                Schema: TrimBrackets((string?)x.ColumnRef.Attribute("Schema")),
                Table: TrimBrackets((string?)x.ColumnRef.Attribute(TableAttributeName)),
                Column: (string?)x.ColumnRef.Attribute(ColumnLiteral),
                ConvertedToDataType: (string?)x.Convert.Attribute("DataType") ?? "unknown",
                RangeSeekBound: IsRangeSeekBound(x.Convert, x.ColumnRef),
                Context: ContextOf(x.Convert)))
            .ToList();
    }

    private static readonly HashSet<string> ContextNames = new(StringComparer.Ordinal)
    {
        "Predicate", "SeekPredicates", "SeekPredicateNew", "SeekPredicate", "Residual", "ProbeResidual", "DefinedValue",
    };

    private static string ContextOf(XElement convert) =>
        convert.Ancestors().Select(a => a.Name.LocalName).FirstOrDefault(ContextNames.Contains) ?? "Other";

    private static bool IsNamedColumn(XElement columnRef)
    {
        var name = (string?)columnRef.Attribute(ColumnLiteral);
        return !string.IsNullOrEmpty(name) && name[0] != '@' && !name.StartsWith("Expr", StringComparison.Ordinal);
    }

    private static string? TrimBrackets(string? bracketedIdentifier) =>
        bracketedIdentifier?.Trim('[', ']');

    private static bool IsRangeSeekBound(XElement convert, XElement columnRef)
    {
        var owningRelOp = convert.Ancestors(ShowPlanNs + "RelOp").FirstOrDefault();
        if (owningRelOp is null)
        {
            return false;
        }

        var database = (string?)columnRef.Attribute("Database");
        var schema = (string?)columnRef.Attribute("Schema");
        var table = (string?)columnRef.Attribute(TableAttributeName);
        var column = (string?)columnRef.Attribute(ColumnLiteral);

        return owningRelOp.Descendants(ShowPlanNs + "SeekPredicates")
            .Concat(owningRelOp.Descendants(ShowPlanNs + "SeekPredicate"))
            .Descendants(ShowPlanNs + "RangeColumns")
            .Descendants(ShowPlanNs + "ColumnReference")
            .Any(rangeColumnRef =>
                string.Equals((string?)rangeColumnRef.Attribute("Database"), database, StringComparison.Ordinal)
                && string.Equals((string?)rangeColumnRef.Attribute("Schema"), schema, StringComparison.Ordinal)
                && string.Equals((string?)rangeColumnRef.Attribute(TableAttributeName), table, StringComparison.Ordinal)
                && string.Equals((string?)rangeColumnRef.Attribute(ColumnLiteral), column, StringComparison.Ordinal));
    }

    private static bool IsImplicit(string? value) => value is "1" or "true";
}

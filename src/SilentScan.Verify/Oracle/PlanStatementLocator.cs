using System.Xml.Linq;

namespace SilentScan.Verify.Oracle;

public static class PlanStatementLocator
{
    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private const string StmtSimpleName = "StmtSimple";
    private const string StmtCondName = "StmtCond";
    private const string StatementTextAttribute = "StatementText";
    private const string StatementTypeAttribute = "StatementType";
    private const string ExecuteProcStatementType = "EXECUTE PROC";

    private const int CutShortTextLength = 3990;

    private static readonly char[] LeadingTrimChars = [' ', '\t', '\r', '\n', ';'];

    public static IReadOnlyList<PlannedStatement> Locate(string moduleDefinition, string planXml)
    {
        var doc = XDocument.Parse(planXml);

        var statements = doc.Descendants()
            .Where(IsBodyStatement)
            .ToList();

        var results = new List<PlannedStatement>(statements.Count);
        var cutShort = new HashSet<int>();
        var searchFrom = 0;

        foreach (var statement in statements)
        {
            var rawText = (string)statement.Attribute(StatementTextAttribute)!;
            var body = rawText.TrimStart(LeadingTrimChars).TrimEnd();
            var index = body.Length == 0 ? -1 : moduleDefinition.IndexOf(body, searchFrom, StringComparison.Ordinal);

            if (index < 0)
            {
                results.Add(new PlannedStatement(body, null, null, ConversionsOf(statement)));
                continue;
            }

            searchFrom = index + body.Length;
            if (rawText.Length >= CutShortTextLength)
            {
                cutShort.Add(results.Count);
            }

            results.Add(new PlannedStatement(
                body,
                LineOf(moduleDefinition, index),
                LineOf(moduleDefinition, index + body.Length - 1),
                ConversionsOf(statement)));
        }

        foreach (var position in cutShort)
        {
            results[position] = results[position] with { EndLine = LastLineBeforeNextLocated(results, position, LineOf(moduleDefinition, moduleDefinition.Length)) };
        }

        return results;
    }

    private static int LastLineBeforeNextLocated(List<PlannedStatement> statements, int position, int lastLine)
    {
        var start = statements[position].StartLine!.Value;
        for (var next = position + 1; next < statements.Count; next++)
        {
            if (statements[next].StartLine is { } nextStart)
            {
                return Math.Max(start, nextStart - 1);
            }
        }

        return lastLine;
    }

    private static bool IsBodyStatement(XElement element) =>
        IsStatementElement(element)
        && element.Attribute(StatementTextAttribute) is not null
        && (string?)element.Attribute(StatementTypeAttribute) != ExecuteProcStatementType;

    private static bool IsStatementElement(XElement element) =>
        element.Name == ShowPlanNs + StmtSimpleName || element.Name == ShowPlanNs + StmtCondName;

    private static IReadOnlyList<ConvertImplicitFinding> ConversionsOf(XElement statement)
    {
        var scoped = new XElement(statement);

        foreach (var nested in scoped.Descendants().Where(IsStatementElement).ToList())
        {
            nested.Remove();
        }

        return ConvertImplicitDetector.FindColumnConversionsIncludingUnattributed(scoped.ToString());
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

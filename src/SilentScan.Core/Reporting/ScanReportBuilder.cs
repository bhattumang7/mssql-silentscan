using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting;

public static class ScanReportBuilder
{
    public static ScanReport Build(IReadOnlyList<string> sqlFilePaths)
    {
        var parser = new SqlScriptParser();
        var fileHealth = new List<FileParseHealth>();
        var findings = new List<SargabilityFinding>();

        foreach (var path in sqlFilePaths)
        {
            var result = parser.ParseFile(path);
            var errors = result.Errors
                .Select(e => new ParseErrorInfo(e.Line, e.Column, e.Number, e.Message))
                .ToList();
            fileHealth.Add(new FileParseHealth(path, errors));

            if (errors.Count == 0)
            {
                findings.AddRange(NonSargablePredicateScanner.Scan(result));
            }
        }

        // Deterministic output ordering (CLAUDE.md).
        findings = [.. findings.OrderBy(f => f.SourcePath, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Column)];

        return new ScanReport(new ParseHealthReport(fileHealth), findings);
    }
}

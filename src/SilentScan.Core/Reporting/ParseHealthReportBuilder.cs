using SilentScan.Core.Parsing;

namespace SilentScan.Core.Reporting;

public static class ParseHealthReportBuilder
{
    public static ParseHealthReport Build(IReadOnlyList<string> sqlFilePaths)
    {
        var parser = new SqlScriptParser();
        var files = sqlFilePaths
            .Select(path =>
            {
                var result = parser.ParseFile(path);
                var errors = result.Errors
                    .Select(e => new ParseErrorInfo(e.Line, e.Column, e.Number, e.Message))
                    .ToList();
                return new FileParseHealth(path, errors);
            })
            .ToList();

        return new ParseHealthReport(files);
    }
}

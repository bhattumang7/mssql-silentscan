namespace SilentScan.Core.Reporting;

/// <summary>
/// Result of Pass 0: did every .sql file in a corpus parse cleanly under ScriptDOM.
/// This is the corpus dialect-sniffing signal from CLAUDE.md ("ScriptDOM parse success
/// rate >= 90% of files"), computed here rather than assumed.
/// </summary>
public sealed record ParseHealthReport(IReadOnlyList<FileParseHealth> Files)
{
    public int TotalFiles => Files.Count;

    public int FilesWithErrors => Files.Count(f => f.Errors.Count > 0);

    public double ParseSuccessRate => TotalFiles == 0 ? 1.0 : (double)(TotalFiles - FilesWithErrors) / TotalFiles;
}

public sealed record FileParseHealth(string Path, IReadOnlyList<ParseErrorInfo> Errors);

public sealed record ParseErrorInfo(int Line, int Column, int Number, string Message);

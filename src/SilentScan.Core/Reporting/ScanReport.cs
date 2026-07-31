using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting;

public sealed record ScanReport(ParseHealthReport ParseHealth, IReadOnlyList<SargabilityFinding> Findings);

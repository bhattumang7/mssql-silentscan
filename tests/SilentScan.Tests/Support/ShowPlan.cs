using System.Xml.Linq;

namespace SilentScan.Tests.Support;

public static class ShowPlan
{
    public static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static IReadOnlyList<string> PhysicalOps(XDocument plan) =>
        [.. plan.Descendants(Ns + "RelOp").Select(op => (string)op.Attribute("PhysicalOp")!)];

    public static bool HasPhysicalOp(XDocument plan, string physicalOp) =>
        PhysicalOps(plan).Contains(physicalOp, StringComparer.Ordinal);

    public static IReadOnlySet<string> IndexNames(XDocument plan) =>
        plan.Descendants(Ns + "RelOp")
            .SelectMany(op => op.Elements().SelectMany(e => e.Elements(Ns + "Object")))
            .Select(o => (string?)o.Attribute("Index"))
            .Where(n => n is not null)
            .Select(n => n!.Trim('[', ']'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> TableNames(XDocument plan) =>
        plan.Descendants(Ns + "Object")
            .Select(o => (string?)o.Attribute("Table"))
            .Where(n => n is not null)
            .Select(n => n!.Trim('[', ']'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool HasConvertImplicit(XDocument plan) =>
        plan.ToString().Contains("CONVERT_IMPLICIT", StringComparison.Ordinal);

    public static string? NonParallelPlanReason(XDocument plan) =>
        plan.Descendants(Ns + "QueryPlan").Select(q => (string?)q.Attribute("NonParallelPlanReason")).FirstOrDefault(r => r is not null);

    public static IEnumerable<XElement> Ops(XDocument plan, string physicalOp) =>
        plan.Descendants(Ns + "RelOp").Where(op => (string)op.Attribute("PhysicalOp")! == physicalOp);

    public static double EstimatedRows(XElement relOp) =>
        double.Parse((string)relOp.Attribute("EstimateRows")!, System.Globalization.CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> PhysicalOpsOnTable(XDocument plan, string table) =>
        [.. plan.Descendants(Ns + "RelOp")
            .Where(op => op.Elements().SelectMany(e => e.Elements(Ns + "Object"))
                .Any(o => string.Equals(((string?)o.Attribute("Table"))?.Trim('[', ']'), table, StringComparison.OrdinalIgnoreCase)))
            .Select(op => (string)op.Attribute("PhysicalOp")!)];

    public static bool HasKeyLookup(XDocument plan) =>
        plan.Descendants(Ns + "IndexScan").Any(scan => (string?)scan.Attribute("Lookup") is "1" or "true");
}

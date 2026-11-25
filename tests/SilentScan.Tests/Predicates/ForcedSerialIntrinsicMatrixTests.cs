using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Predicates;

public sealed class ForcedSerialIntrinsicMatrixTests
{
    private const string EngineReason = "NonParallelizableIntrinsicFunction";

    private static bool ScannerFlags(BuiltinProbe probe)
    {
        var shape = BuiltinMatrixGenerator.Shapes(probe)[0].Types;
        var expression = BuiltinMatrixGenerator.Expression(probe, shape);
        for (var i = 0; i < probe.Positions.Count; i++)
        {
            expression = expression.Replace("a" + i, "t.a" + i, StringComparison.Ordinal);
        }

        var columns = shape.Count == 0 ? "dummy INT NULL" : string.Join(", ", shape.Select((type, i) => $"a{i} {type} NULL"));
        var sql = $"CREATE TABLE dbo.tt ({columns});\nGO\nCREATE PROCEDURE dbo.p AS SELECT {expression} FROM dbo.tt AS t;";
        var result = SqlScriptParser.ParseText("t.sql", sql);
        if (result.HasErrors)
        {
            return false;
        }

        return ForcedSerialScanner.Scan(result).Any(f => f.Kind == ForcedSerialFindingKind.NonParallelizableIntrinsic);
    }

    [Fact]
    public void ScannerFlagsExactlyTheFunctionsTheEnginePlansSerialBecauseOfAnIntrinsic()
    {
        var disagreements = new List<string>();
        var compared = 0;
        var serial = 0;
        var sources = BuiltinProbes.All.Select(p => (Probe: p, Matrix: BuiltinMatrix.Instance))
            .Concat(BuiltinProbes.LatestOnly.Select(p => (Probe: p, Matrix: BuiltinMatrix.Latest)));
        foreach (var (probe, matrix) in sources.GroupBy(s => s.Probe.Function).Select(g => g.First()))
        {
            var cells = matrix.ForFunction(probe.Function);
            if (cells.Count == 0 || cells.Any(c => c.Parallel.StartsWith(BuiltinMatrix.ErrorPrefix, StringComparison.Ordinal)))
            {
                continue;
            }

            var engineSerial = cells.Any(c => c.Parallel == EngineReason);
            compared++;
            serial += engineSerial ? 1 : 0;
            var flagged = ScannerFlags(probe);
            if (engineSerial != flagged)
            {
                disagreements.Add($"{probe.Function}: engine {(engineSerial ? "forces serial" : "allows parallel")}, scanner {(flagged ? "flags" : "does not flag")}");
            }
        }

        Assert.True(disagreements.Count == 0, string.Join("\n", disagreements));
    }
}

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SilentScan.NoCommentsAnalyzer;

namespace SilentScan.Tests.Architecture;

public sealed class OraclePairingAnalyzerTests
{
    private const string Prelude = """
        namespace SilentScan.Core.Rules
        {
            public static class FindingRuleIds
            {
                public const string AlphaRuleId = "silentscan/x/alpha";
                public const string BetaRuleId = "silentscan/x/beta";
            }
        }

        namespace SilentScan.Core.Reporting
        {
            public static class ExampleScanner
            {
                public static int Go() => 1;
            }
        }

        namespace SilentScan.Tests.Support
        {
            public abstract class RuleOracle
            {
            }
        }

        namespace SilentScan.Verify.Oracle
        {
            public abstract class RuleVerifier<TFinding>
            {
                protected abstract System.Threading.Tasks.Task<bool> ObserveAsync(object connection, TFinding finding);
            }
        }
        """;

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string source, string baseline = "")
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(p => MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(TraitAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(Prelude), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = new AnalyzerOptions([new InMemoryAdditionalText("oracle-pairing-baseline.txt", baseline)]);
        var withAnalyzers = compilation.WithAnalyzers([new OraclePairingAnalyzer()], options);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.Where(d => d.Id.StartsWith("SS001", StringComparison.Ordinal))];
    }

    private static string PairedAlpha() =>
        """
        [Xunit.Trait("Rule", SilentScan.Core.Rules.FindingRuleIds.AlphaRuleId)]
        public sealed class AlphaOracle : SilentScan.Tests.Support.RuleOracle
        {
        }
        """;

    private static string VerifierObserving(string body) =>
        string.Create(CultureInfo.InvariantCulture, $$"""
            public sealed class AlphaVerifier : SilentScan.Verify.Oracle.RuleVerifier<string>
            {
                protected override System.Threading.Tasks.Task<bool> ObserveAsync(object connection, string finding)
                {
                    {{body}}
                }
            }
            """);

    [Fact]
    public async Task LiteralRuleTrait_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            [Xunit.Trait("Rule", "silentscan/x/alpha")]
            public sealed class RawOracleTests { }
            """,
            "AlphaRuleId\nBetaRuleId\n");

        Assert.Contains(diagnostics, d => d.Id == OraclePairingAnalyzer.RawRuleTraitId);
    }

    [Fact]
    public async Task ConstantRuleTrait_OnABaselinedRule_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            """
            [Xunit.Trait("Rule", SilentScan.Core.Rules.FindingRuleIds.AlphaRuleId)]
            public sealed class RawOracleTests { }
            """,
            "AlphaRuleId\nBetaRuleId\n");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RuleOracleSubclass_WithoutRuleTrait_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            """
            public sealed class NoTraitOracle : SilentScan.Tests.Support.RuleOracle
            {
            }
            """,
            "AlphaRuleId\nBetaRuleId\n");

        Assert.Contains(diagnostics, d => d.Id == OraclePairingAnalyzer.RawRuleTraitId);
    }

    [Fact]
    public async Task PublishedRuleWithNoPairedTestAndNoBaselineEntry_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(PairedAlpha(), "");

        var unpaired = Assert.Single(diagnostics, d => d.Id == OraclePairingAnalyzer.UnpairedRuleId);
        Assert.Contains("BetaRuleId", unpaired.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairedRuleAndBaselinedRule_ProduceNoDiagnostics()
    {
        var diagnostics = await AnalyzeAsync(PairedAlpha(), "BetaRuleId\n");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BaselineEntryForARuleThatIsNowPaired_IsStale()
    {
        var diagnostics = await AnalyzeAsync(PairedAlpha(), "AlphaRuleId\nBetaRuleId\n");

        var stale = Assert.Single(diagnostics);
        Assert.Equal(OraclePairingAnalyzer.StaleBaselineId, stale.Id);
        Assert.Contains("AlphaRuleId", stale.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaselineEntryNamingNoConstant_IsStale()
    {
        var diagnostics = await AnalyzeAsync(PairedAlpha(), "BetaRuleId\nGammaRuleId\n");

        var stale = Assert.Single(diagnostics);
        Assert.Equal(OraclePairingAnalyzer.StaleBaselineId, stale.Id);
        Assert.Contains("GammaRuleId", stale.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifierObservationReferencingScannerCode_IsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            PairedAlpha() + "\n" + VerifierObserving("var n = SilentScan.Core.Reporting.ExampleScanner.Go(); return System.Threading.Tasks.Task.FromResult(n == 1);"),
            "BetaRuleId\n");

        Assert.Contains(diagnostics, d => d.Id == OraclePairingAnalyzer.EngineSideUsesScannerId);
    }

    [Fact]
    public async Task VerifierObservationNotUsingScannerCode_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            PairedAlpha() + "\n" + VerifierObserving("return System.Threading.Tasks.Task.FromResult(true);"),
            "BetaRuleId\n");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ScannerNamespaceOutsideAVerifier_IsNotRejected()
    {
        var diagnostics = await AnalyzeAsync(
            PairedAlpha() + """

            public static class Elsewhere
            {
                public static int Use() => SilentScan.Core.Reporting.ExampleScanner.Go();
            }
            """,
            "BetaRuleId\n");

        Assert.Empty(diagnostics);
    }
}

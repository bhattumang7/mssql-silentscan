using System.Net;
using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.Readable;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Reporting;

public sealed class HtmlDocumentRendererTests
{
    private const string HostileTable = "dbo.<script>alert(\"x\")</script>&'T'";

    private static string Html(params ReadableBlock[] blocks) =>
        ReadableDocumentRenderer.Render(new ReadableDocument(blocks), ReadableStyle.Html);

    private static ScanReport SyntheticReport()
    {
        var typed = new TypedPredicateFinding(
            Verdict.ScanForced,
            new PredicateOperand.Column(HostileTable, "Code", new SqlType(SqlTypeCategory.VarChar), Indexed: true, Depth: 0, Provenance: null!),
            new PredicateOperand.Value(null),
            "=",
            "dbo.usp_Find",
            4,
            9);
        var sargable = new SargabilityFinding(
            SargabilityFindingKind.FunctionWrappedColumn, "Col & \"quoted\"", "<b>bold</b>", "dbo.usp_Other", 7, 3);
        var dynamicSql = new DynamicSqlFinding("dbo.usp_Dynamic", 12, 5, DynamicSqlOutcome.Unanalyzable, "non-literal-argument");

        return TestScanReports.Build(
            TypedFindings: [typed],
            Tier1Findings: [sargable],
            DynamicSqlFindings: [dynamicSql],
            TypedPredicateSummary: TypedPredicateSummary.From([typed]),
            DynamicSqlSummary: DynamicSqlSummary.From([dynamicSql]));
    }

    private static string SectionId(string html, string headingText)
    {
        var match = Regex.Match(
            html,
            $"<section class=\"sec\" id=\"(?<id>s\\d+)\"(?:(?!<section ).)*?<h2>{Regex.Escape(WebUtility.HtmlEncode(headingText))}</h2>",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        Assert.True(match.Success, $"no section headed '{headingText}'");
        return match.Groups["id"].Value;
    }

    private static int Occurrences(string haystack, string needle) =>
        Regex.Count(haystack, Regex.Escape(needle), RegexOptions.None, TimeSpan.FromSeconds(5));

    [Fact]
    public void Html_IsOneSelfContainedDocumentWithNoNetworkReferences()
    {
        var html = Html(new ReadableBlock.Heading(1, "Report"), new ReadableBlock.Paragraph("body"));

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>", html.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("<title>Report</title>", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<style>"));
        Assert.Equal(1, Occurrences(html, "<script>"));
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Html_HostileTextInEveryBlockKind_IsEncodedNotInterpreted()
    {
        var hostile = "<script>alert(\"x\")</script> & 'q'";

        var html = Html(
            new ReadableBlock.Heading(1, hostile),
            new ReadableBlock.Heading(2, hostile),
            new ReadableBlock.Paragraph(hostile),
            new ReadableBlock.Table([hostile], [[hostile]]),
            new ReadableBlock.Bullets([hostile]));

        const string Encoded = "&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;q&#39;";
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<script>"));
        Assert.Contains($"<title>{Encoded}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<h1>{Encoded}</h1>", html, StringComparison.Ordinal);
        Assert.Contains($"<h2>{Encoded}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<p>{Encoded}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"<td>{Encoded}</td>", html, StringComparison.Ordinal);
        Assert.Contains($">{Encoded}</button></th>", html, StringComparison.Ordinal);
        Assert.Contains($"<li>{Encoded}</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_Urls_BecomeLinksAndCannotBreakOutOfTheHrefAttribute()
    {
        var url = RuleDocSite.Url(SarifRuleCatalog.ExpressionDerivedRuleId);

        var html = Html(
            new ReadableBlock.Paragraph(url),
            new ReadableBlock.Table(["A"], [["see https://example.test/a\"onmouseover=\"alert(1) now"]]),
            new ReadableBlock.Paragraph("javascript:alert(1)"));

        Assert.Contains($"<p class=\"doc\"><a href=\"{url}\">{url}</a></p>", html, StringComparison.Ordinal);
        Assert.Contains("<td>see <a href=\"https://example.test/a\">https://example.test/a</a>&quot;onmouseover=&quot;alert(1) now</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.Ordinal);
        Assert.Contains("<p>javascript:alert(1)</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_UrlTrailingPunctuation_StaysOutsideTheLink()
    {
        var html = Html(new ReadableBlock.Paragraph("Read https://example.test/rules/x.html."));

        Assert.Contains("<p>Read <a href=\"https://example.test/rules/x.html\">https://example.test/rules/x.html</a>.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_LargeTable_IsCappedWithAShowAllControlAndASmallOneIsNot()
    {
        IReadOnlyList<string>[] Rows(int count) => [.. Enumerable.Range(1, count).Select(i => (IReadOnlyList<string>)[$"r{i}"])];

        var capped = Html(new ReadableBlock.Heading(2, "Big"), new ReadableBlock.Table(["A"], Rows(501)));
        var uncapped = Html(new ReadableBlock.Heading(2, "Small"), new ReadableBlock.Table(["A"], Rows(500)));

        Assert.Contains("<table class=\"cap\">", capped, StringComparison.Ordinal);
        Assert.Contains(">Show all 501 rows</button>", capped, StringComparison.Ordinal);
        Assert.Equal(502, Occurrences(capped, "<tr>"));
        Assert.Contains("<td>r501</td>", capped, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class=\"cap\">", uncapped, StringComparison.Ordinal);
        Assert.DoesNotContain(">Show all", uncapped, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_SectionsOpenByDefaultOnlyWhileTheirRowCountStaysSmall()
    {
        IReadOnlyList<string>[] Rows(int count) => [.. Enumerable.Range(1, count).Select(i => (IReadOnlyList<string>)[$"r{i}"])];

        var html = Html(
            new ReadableBlock.Heading(2, "Small (300)"),
            new ReadableBlock.Table(["A"], Rows(300)),
            new ReadableBlock.Heading(2, "Large (301)"),
            new ReadableBlock.Table(["A"], Rows(301)));

        Assert.Matches(new Regex("<details open><summary[^>]*><h2>Small \\(300\\)</h2>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
        Assert.Matches(new Regex("<details><summary[^>]*><h2>Large \\(301\\)</h2>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
    }

    [Fact]
    public void Html_SyntheticReport_CarriesEveryHeadingParagraphCellAndRowOfTheMarkdownDocument()
    {
        var document = ReadableScanReportWriter.BuildDocument(SyntheticReport(), "SilentScan - synthetic", verbosity: ReadableVerbosity.Full);

        var html = ReadableDocumentRenderer.Render(document, ReadableStyle.Html);

        var tables = document.Blocks.OfType<ReadableBlock.Table>().ToList();
        Assert.True(tables.Count >= 4, $"expected the summary and three finding tables, got {tables.Count}");
        Assert.Equal(tables.Sum(t => t.Rows.Count + 1), Occurrences(html, "<tr>"));
        Assert.Equal(document.Blocks.OfType<ReadableBlock.Heading>().Count(h => h.Level == 2), Occurrences(html, "<section class=\"sec\""));

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ReadableBlock.Heading heading:
                    Assert.Contains($"<h{heading.Level}>{WebUtility.HtmlEncode(heading.Text)}</h{heading.Level}>", html, StringComparison.Ordinal);
                    break;
                case ReadableBlock.Paragraph paragraph:
                    Assert.Contains(WebUtility.HtmlEncode(paragraph.Text), html, StringComparison.Ordinal);
                    break;
                case ReadableBlock.Table table:
                    Assert.All(table.Headers, h => Assert.Contains($">{WebUtility.HtmlEncode(h)}</button></th>", html, StringComparison.Ordinal));
                    Assert.All(table.Rows.SelectMany(r => r), c => Assert.Contains(WebUtility.HtmlEncode(c), html, StringComparison.Ordinal));
                    break;
                case ReadableBlock.Bullets bullets:
                    Assert.All(bullets.Items, i => Assert.Contains($"<li>{WebUtility.HtmlEncode(i)}</li>", html, StringComparison.Ordinal));
                    break;
            }
        }

        Assert.DoesNotContain(HostileTable, html, StringComparison.Ordinal);
        Assert.Contains("dbo.&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;&amp;&#39;T&#39;.Code", html, StringComparison.Ordinal);
        var tier1Url = RuleDocSite.Url(SarifRuleCatalog.Tier1RuleId(SargabilityFindingKind.FunctionWrappedColumn));
        Assert.Contains($"<a href=\"{tier1Url}\">{tier1Url}</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_SummaryRows_LinkToTheSectionTheyCount()
    {
        var html = ReadableScanReportWriter.Write(SyntheticReport(), "SilentScan - synthetic", ReadableStyle.Html, verbosity: ReadableVerbosity.Full);

        var scanForced = SectionId(html, "Implicit conversions that force a scan (1)");
        var nonSargable = SectionId(html, "Non-sargable predicate patterns (1)");
        var dynamicSql = SectionId(html, "Dynamic SQL not fully analyzed (1)");

        Assert.Equal(3, new[] { scanForced, nonSargable, dynamicSql }.Distinct().Count());
        Assert.Contains($"<a href=\"#{scanForced}\">Implicit conversions forcing a scan</a>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"#{nonSargable}\">Non-sargable predicate patterns</a>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"#{dynamicSql}\">Dynamic SQL call sites not statically analyzable</a>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"#{scanForced}\">", html[html.IndexOf("<nav", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void Html_SummaryRowWithNoSectionOfThatCount_IsNotLinked()
    {
        var html = Html(
            new ReadableBlock.Heading(2, ReadableScanReportWriter.SummaryHeading),
            new ReadableBlock.Table(["What", "Occurrences", "Distinct"], [["Widgets found", "2", "-"]]),
            new ReadableBlock.Heading(2, "Widgets found (3)"));

        Assert.Contains("<th scope=\"row\">Widgets found</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Widgets found</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_EmptyReport_RendersTheNoFindingsSummaryWithoutTables()
    {
        var html = ReadableScanReportWriter.Write(TestScanReports.Build(), "SilentScan - empty", ReadableStyle.Html);

        Assert.Contains("<h1>SilentScan - empty</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Summary</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<p>No findings.</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<section class=\"sec\""));
    }
}

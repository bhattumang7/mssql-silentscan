using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentScan.Core.Reporting.Readable;

internal static partial class HtmlDocumentRenderer
{
    private const int RowCap = 500;

    private const int OpenSectionRowLimit = 300;

    private const int FilterRowThreshold = 10;

    private const string DefaultTitle = "SilentScan report";

    private static readonly char[] TrailingUrlPunctuation = ['.', ',', ';', ':', '!', '?', ')', ']', '}'];

    private static readonly Lazy<string> Stylesheet = new(() => LoadAsset("HtmlReport.css"));

    private static readonly Lazy<string> Script = new(() => LoadAsset("HtmlReport.js"));

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "that", "not", "into", "its", "own", "are", "from", "has", "can", "who", "via",
    };

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"^(?<title>.*) \((?<count>\d+)\)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CountedHeadingPattern();

    [GeneratedRegex(@"[a-z0-9_]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();

    public static string Render(ReadableDocument document)
    {
        var outline = Outline.Build(document.Blocks);
        var html = new StringBuilder(4096 + (outline.TotalRows * 160));

        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"color-scheme\" content=\"light dark\">\n")
            .Append("<title>").Append(Encode(outline.Title)).Append("</title>\n")
            .Append("<style>").Append(Stylesheet.Value).Append("</style>\n</head>\n<body>\n");

        AppendHeader(html, outline.Title);
        html.Append("<div class=\"layout\">\n");
        AppendToc(html, outline);
        html.Append("<main id=\"main\" tabindex=\"-1\">\n");

        foreach (var block in outline.Intro)
        {
            AppendBlock(html, block, outline, isSummary: false);
        }

        foreach (var section in outline.Sections)
        {
            AppendSection(html, section, outline);
        }

        html.Append("</main>\n</div>\n<script>").Append(Script.Value).Append("</script>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static void AppendHeader(StringBuilder html, string title)
    {
        html.Append("<a class=\"skip\" href=\"#main\">Skip to the report</a>\n")
            .Append("<header class=\"bar\">\n")
            .Append("<button type=\"button\" class=\"btn toc-toggle\" id=\"toc-toggle\" aria-controls=\"toc\" aria-expanded=\"false\">Contents</button>\n")
            .Append("<div class=\"brand\"><span class=\"eyebrow\">SilentScan</span><h1>").Append(Encode(title)).Append("</h1></div>\n")
            .Append("<div class=\"find\" role=\"search\"><label class=\"vh\" for=\"q\">Search every section</label>")
            .Append("<input id=\"q\" type=\"search\" placeholder=\"Search all findings\" autocomplete=\"off\" spellcheck=\"false\">")
            .Append("<kbd aria-hidden=\"true\">/</kbd></div>\n")
            .Append("<output id=\"qs\" class=\"qs\" for=\"q\" aria-live=\"polite\"></output>\n")
            .Append("<div class=\"acts\">")
            .Append("<button type=\"button\" class=\"btn\" id=\"expand-all\">Expand all</button>")
            .Append("<button type=\"button\" class=\"btn\" id=\"collapse-all\">Collapse all</button>")
            .Append("<button type=\"button\" class=\"btn\" id=\"theme\" aria-live=\"polite\">Theme: auto</button>")
            .Append("</div>\n</header>\n");
    }

    private static void AppendToc(StringBuilder html, Outline outline)
    {
        html.Append("<nav id=\"toc\" class=\"toc\" aria-label=\"Sections\">\n<ul>\n");

        foreach (var section in outline.Sections)
        {
            html.Append("<li>");
            AppendTocLink(html, section.Id, section.Heading.Text);

            if (section.SubHeadings.Count > 0)
            {
                html.Append("<ul>");
                foreach (var sub in section.SubHeadings)
                {
                    html.Append("<li>");
                    AppendTocLink(html, outline.IdOf(sub), sub.Text);
                    html.Append("</li>");
                }

                html.Append("</ul>");
            }

            html.Append("</li>\n");
        }

        html.Append("</ul>\n</nav>\n");
    }

    private static void AppendTocLink(StringBuilder html, string id, string text)
    {
        var (title, count) = SplitCount(text);
        html.Append("<a href=\"#").Append(id).Append("\"><span>").Append(Encode(title)).Append("</span>");

        if (count is { } n)
        {
            html.Append("<span class=\"pill\">").Append(FormatCount(n)).Append("</span>");
        }

        html.Append("</a>");
    }

    private static void AppendSection(StringBuilder html, Section section, Outline outline)
    {
        var isSummary = section.Heading.Text == ReadableScanReportWriter.SummaryHeading;
        var open = isSummary || section.Rows <= OpenSectionRowLimit;

        html.Append("<section class=\"sec\" id=\"").Append(section.Id).Append('"');
        if (isSummary)
        {
            html.Append(" data-kind=\"summary\"");
        }

        html.Append(">\n<details").Append(open ? " open" : string.Empty).Append("><summary>")
            .Append("<h2>").Append(Encode(section.Heading.Text)).Append("</h2>");

        if (section.Rows > 0 && !isSummary)
        {
            html.Append("<span class=\"rows\">").Append(RowsLabel(section.Rows)).Append("</span>");
        }

        html.Append("</summary>\n<div class=\"body\">\n");

        foreach (var block in section.Body)
        {
            AppendBlock(html, block, outline, isSummary);
        }

        html.Append("</div>\n</details>\n</section>\n");
    }

    private static void AppendBlock(StringBuilder html, ReadableBlock block, Outline outline, bool isSummary)
    {
        switch (block)
        {
            case ReadableBlock.Heading heading:
                AppendSubHeading(html, heading, outline.IdOf(heading));
                break;
            case ReadableBlock.Paragraph paragraph:
                AppendParagraph(html, paragraph.Text);
                break;
            case ReadableBlock.Table table when isSummary && IsSummaryTable(table):
                AppendSummaryTable(html, table, outline);
                break;
            case ReadableBlock.Table table:
                AppendDataTable(html, table);
                break;
            case ReadableBlock.Bullets bullets:
                AppendBullets(html, bullets);
                break;
        }
    }

    private static void AppendSubHeading(StringBuilder html, ReadableBlock.Heading heading, string id)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        html.Append("<div class=\"sub\" id=\"").Append(id).Append("\"><h").Append(level).Append('>')
            .Append(Encode(heading.Text)).Append("</h").Append(level).Append("></div>\n");
    }

    private static void AppendBullets(StringBuilder html, ReadableBlock.Bullets bullets)
    {
        html.Append("<ul class=\"list\">");
        foreach (var item in bullets.Items)
        {
            html.Append("<li>");
            AppendInline(html, item);
            html.Append("</li>");
        }

        html.Append("</ul>\n");
    }

    private static void AppendParagraph(StringBuilder html, string text)
    {
        var trimmed = text.Trim();
        var match = UrlPattern().Match(trimmed);
        if (match.Success && match.Length == trimmed.Length)
        {
            html.Append("<p class=\"doc\">");
            AppendLink(html, trimmed);
            html.Append("</p>\n");
            return;
        }

        html.Append("<p>");
        AppendInline(html, text);
        html.Append("</p>\n");
    }

    private static bool IsSummaryTable(ReadableBlock.Table table) =>
        table.Headers.Count == 3 && table.Headers[0] == ReadableScanReportWriter.SummaryWhatHeader;

    private static void AppendSummaryTable(StringBuilder html, ReadableBlock.Table table, Outline outline)
    {
        var max = table.Rows.Select(r => ParseCount(r[1]) ?? 0).DefaultIfEmpty(0).Max();

        html.Append("<div class=\"tbl dash\">\n<div class=\"scroll\"><table class=\"bars\">");
        AppendHead(html, table.Headers);
        html.Append("<tbody>\n");

        foreach (var row in table.Rows)
        {
            var occurrences = ParseCount(row[1]);
            var target = occurrences is { } n ? outline.SectionFor(row[0], n) : null;

            html.Append("<tr><th scope=\"row\">");
            if (target is null)
            {
                html.Append(Encode(row[0]));
            }
            else
            {
                html.Append("<a href=\"#").Append(target.Id).Append("\">").Append(Encode(row[0])).Append("</a>");
            }

            var width = max > 0 && occurrences is { } value ? value * 100.0 / max : 0;
            html.Append("</th><td class=\"val\"><span class=\"track\" aria-hidden=\"true\"><span class=\"fill\" style=\"width:")
                .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append("%\"></span></span><span class=\"num\">")
                .Append(Encode(row[1])).Append("</span></td><td class=\"num\">").Append(Encode(row[2])).Append("</td></tr>\n");
        }

        html.Append("</tbody></table></div>\n</div>\n");
    }

    private static void AppendDataTable(StringBuilder html, ReadableBlock.Table table)
    {
        var rows = table.Rows.Count;
        var capped = rows > RowCap;

        html.Append("<div class=\"tbl\" data-rows=\"").Append(rows.ToString(CultureInfo.InvariantCulture)).Append("\">\n<div class=\"tools\">");
        if (rows > FilterRowThreshold)
        {
            html.Append("<input type=\"search\" class=\"flt\" placeholder=\"Filter ").Append(RowsLabel(rows))
                .Append("\" aria-label=\"Filter the rows of this table\" autocomplete=\"off\" spellcheck=\"false\">");
        }

        html.Append("<span class=\"cnt\">").Append(RowsLabel(rows)).Append("</span>");
        if (capped)
        {
            html.Append("<span class=\"capnote\">first ").Append(FormatCount(RowCap)).Append(" shown</span>")
                .Append("<button type=\"button\" class=\"btn more\">Show all ").Append(RowsLabel(rows)).Append("</button>");
        }

        html.Append("</div>\n<div class=\"scroll\"><table").Append(capped ? " class=\"cap\"" : string.Empty).Append('>');
        AppendHead(html, table.Headers);
        html.Append("<tbody>\n");

        foreach (var row in table.Rows)
        {
            html.Append("<tr>");
            foreach (var cell in row)
            {
                html.Append("<td>");
                AppendInline(html, cell);
                html.Append("</td>");
            }

            html.Append("</tr>\n");
        }

        html.Append("</tbody></table></div>\n</div>\n");
    }

    private static void AppendHead(StringBuilder html, IReadOnlyList<string> headers)
    {
        html.Append("<thead><tr>");
        foreach (var header in headers)
        {
            html.Append("<th scope=\"col\"><button type=\"button\" class=\"sort\">").Append(Encode(header)).Append("</button></th>");
        }

        html.Append("</tr></thead>");
    }

    private static void AppendInline(StringBuilder html, string text)
    {
        var position = 0;

        foreach (Match match in UrlPattern().Matches(text))
        {
            var url = match.Value.TrimEnd(TrailingUrlPunctuation);
            html.Append(Encode(text[position..match.Index]));
            AppendLink(html, url);
            position = match.Index + url.Length;
        }

        html.Append(Encode(text[position..]));
    }

    private static void AppendLink(StringBuilder html, string url)
    {
        var encoded = Encode(url);
        html.Append("<a href=\"").Append(encoded).Append("\">").Append(encoded).Append("</a>");
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string RowsLabel(int rows) => rows == 1 ? "1 row" : $"{FormatCount(rows)} rows";

    private static string FormatCount(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static int? ParseCount(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static (string Title, int? Count) SplitCount(string headingText)
    {
        var match = CountedHeadingPattern().Match(headingText);
        return match.Success && ParseCount(match.Groups["count"].Value) is { } count
            ? (match.Groups["title"].Value, count)
            : (headingText, null);
    }

    private static HashSet<string> Stems(string text) =>
        WordPattern().Matches(text.ToLowerInvariant())
            .Select(word => word.Value)
            .Where(word => word.Length >= 3 && !StopWords.Contains(word))
            .Select(word => word.Length > 4 ? word[..4] : word)
            .ToHashSet(StringComparer.Ordinal);

    private static string LoadAsset(string name)
    {
        var assembly = typeof(HtmlDocumentRenderer).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Reporting.Readable.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the HTML report assets are missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed record Section(string Id, ReadableBlock.Heading Heading, int? Count, HashSet<string> Stems, List<ReadableBlock> Body, List<ReadableBlock.Heading> SubHeadings)
    {
        public int Rows { get; set; }
    }

    private sealed class Outline
    {
        private readonly Dictionary<ReadableBlock.Heading, string> _ids = new(ReferenceEqualityComparer.Instance);

        private Outline(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public List<ReadableBlock> Intro { get; } = [];

        public List<Section> Sections { get; } = [];

        public int TotalRows { get; private set; }

        public static Outline Build(IReadOnlyList<ReadableBlock> blocks)
        {
            var titleHeading = blocks.Count > 0 && blocks[0] is ReadableBlock.Heading { Level: 1 } first ? first : null;
            var outline = new Outline(titleHeading?.Text ?? DefaultTitle);
            List<ReadableBlock> rest = titleHeading is null ? [.. blocks] : [.. blocks.Skip(1)];
            var sectionLevel = rest.OfType<ReadableBlock.Heading>().Select(h => h.Level).DefaultIfEmpty(2).Min();

            Section? current = null;
            foreach (var block in rest)
            {
                current = outline.Place(block, current, sectionLevel);
            }

            return outline;
        }

        public string IdOf(ReadableBlock.Heading heading) => _ids[heading];

        public Section? SectionFor(string label, int occurrences)
        {
            var labelStems = Stems(label);
            var ranked = Sections
                .Where(s => s.Count == occurrences && s.Heading.Text != ReadableScanReportWriter.SummaryHeading)
                .Select(s => (Section: s, Score: s.Stems.Count(labelStems.Contains)))
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score)
                .Take(2)
                .ToList();

            return ranked.Count switch
            {
                0 => null,
                1 => ranked[0].Section,
                _ => ranked[0].Score > ranked[1].Score ? ranked[0].Section : null,
            };
        }

        private Section? Place(ReadableBlock block, Section? current, int sectionLevel)
        {
            if (block is ReadableBlock.Heading heading)
            {
                var id = $"s{(_ids.Count + 1).ToString(CultureInfo.InvariantCulture)}";
                _ids[heading] = id;

                if (heading.Level <= sectionLevel)
                {
                    var (title, count) = SplitCount(heading.Text);
                    var section = new Section(id, heading, count, Stems(title), [], []);
                    Sections.Add(section);
                    return section;
                }

                current?.SubHeadings.Add(heading);
            }

            if (block is ReadableBlock.Table table)
            {
                TotalRows += table.Rows.Count;
                if (current is not null)
                {
                    current.Rows += table.Rows.Count;
                }
            }

            (current?.Body ?? Intro).Add(block);
            return current;
        }
    }
}

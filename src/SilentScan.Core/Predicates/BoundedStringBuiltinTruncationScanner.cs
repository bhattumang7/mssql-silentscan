using System.Buffers;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class BoundedStringBuiltinTruncationScanner
{
    private const int NonUnicodeCapBytes = 8000;
    private const int UnicodeCapBytes = 4000;

    public static IReadOnlyList<BoundedStringBuiltinTruncationFinding> Scan(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        var scanned = catalog ?? new DatabaseCatalog();
        var rule = CreateRule(parseResult.SourcePath, scanned);
        var walker = new ModuleWalker(parseResult.SourcePath, scanned, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog.DefaultCollation);

    internal static IReadOnlyList<BoundedStringBuiltinTruncationFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, Collation? databaseCollation) : IModuleRule
    {
        public List<BoundedStringBuiltinTruncationFinding> Findings { get; } = [];

        public void OnEnterFunctionCall(FunctionCall node, ModuleWalker walker)
        {
            var name = node.FunctionName?.Value;

            if (string.Equals(name, "REPLICATE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 2)
            {
                CheckReplicate(node);
            }
            else if (string.Equals(name, "REPLACE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 3)
            {
                CheckReplace(node);
            }
            else if (string.Equals(name, "REGEXP_REPLACE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 3)
            {
                CheckRegexpReplace(node);
            }
            else if (string.Equals(name, "SPACE", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count == 1)
            {
                CheckSpace(node);
            }
        }

        private void CheckReplicate(FunctionCall node)
        {
            if (node.Parameters[0] is not StringLiteral source)
            {
                return;
            }

            if (LiteralComparisonFolder.TryFoldToNumeric(node.Parameters[1]) is not { } rawCount
                || decimal.Truncate(rawCount) is not (> 0 and var countValue))
            {
                return;
            }

            var cap = source.IsNational ? UnicodeCapBytes : NonUnicodeCapBytes;
            var length = LengthInCapUnits(source.Value, source.IsNational);
            if (length > cap)
            {
                return;
            }

            var computed = length * (long)countValue;
            if (computed <= cap)
            {
                return;
            }

            Add(BoundedStringBuiltinTruncationFindingKind.ReplicateResultTruncated, "REPLICATE", computed, cap, node);
        }

        private static readonly SearchValues<char> RegexMetacharacters = SearchValues.Create(@"\.^$|?*+()[]{}");

        private void CheckReplace(FunctionCall node) => CheckSubstitution(node, "REPLACE", MatchingComparison);

        private void CheckRegexpReplace(FunctionCall node)
        {
            if (LiteralComparisonFolder.TryFoldToString(node.Parameters[1]) is { } pattern
                && !pattern.AsSpan().ContainsAny(RegexMetacharacters)
                && LiteralComparisonFolder.TryFoldToString(node.Parameters[2])?.Contains('\\') == false)
            {
                CheckSubstitution(node, "REGEXP_REPLACE", _ => StringComparison.Ordinal);
            }
        }

        private void CheckSubstitution(FunctionCall node, string functionName, Func<StringLiteral, StringComparison> comparison)
        {
            if (node.Parameters[0] is not StringLiteral input)
            {
                return;
            }

            var cap = input.IsNational ? UnicodeCapBytes : NonUnicodeCapBytes;
            if (LengthInCapUnits(input.Value, input.IsNational) > cap)
            {
                return;
            }

            if (LiteralComparisonFolder.TryFoldToString(node.Parameters[1]) is not { Length: > 0 } from
                || LiteralComparisonFolder.TryFoldToString(node.Parameters[2]) is not { } to
                || LengthInCapUnits(to, input.IsNational) <= LengthInCapUnits(from, input.IsNational))
            {
                return;
            }

            var replaced = LengthInCapUnits(input.Value.Replace(from, to, comparison(input)), input.IsNational);
            if (replaced <= cap)
            {
                return;
            }

            Add(BoundedStringBuiltinTruncationFindingKind.ReplaceResultTruncated, functionName, replaced, cap, node);
        }

        private long LengthInCapUnits(string value, bool isNational) =>
            !isNational && databaseCollation is { IsUtf8: true } ? Encoding.UTF8.GetByteCount(value) : value.Length;

        private StringComparison MatchingComparison(StringLiteral input)
        {
            var collation = input.Collation is { } explicitCollation ? new Collation(explicitCollation.Value) : databaseCollation;
            return collation is { IsCaseSensitive: false } ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        private StringComparison ReplaceComparison(FunctionCall node)
        {
            var explicitCollation = node.Parameters
                .OfType<PrimaryExpression>()
                .Select(parameter => parameter.Collation?.Value)
                .FirstOrDefault(name => name is not null);
            var collation = explicitCollation is null ? databaseCollation : new Collation(explicitCollation);
            return collation is { IsCaseSensitive: false } && !HasDottedAndDotlessI(collation)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private static bool HasDottedAndDotlessI(Collation collation) =>
            collation.Name.Contains("Turkish", StringComparison.OrdinalIgnoreCase)
            || collation.Name.Contains("Azeri", StringComparison.OrdinalIgnoreCase)
            || collation.Name.Contains("CP1254", StringComparison.OrdinalIgnoreCase);

        private void CheckSpace(FunctionCall node)
        {
            if (LiteralComparisonFolder.TryFoldToNumeric(node.Parameters[0]) is not { } rawCount
                || decimal.Truncate(rawCount) is not (> NonUnicodeCapBytes and var countValue))
            {
                return;
            }

            Add(BoundedStringBuiltinTruncationFindingKind.SpaceResultTruncated, "SPACE", (long)countValue, NonUnicodeCapBytes, node);
        }

        private void Add(BoundedStringBuiltinTruncationFindingKind kind, string functionName, long computedLength, int cap, FunctionCall node) =>
            Findings.Add(new BoundedStringBuiltinTruncationFinding(
                kind, functionName, computedLength, cap,
                sourcePath, node.StartLine, node.StartColumn));
    }
}

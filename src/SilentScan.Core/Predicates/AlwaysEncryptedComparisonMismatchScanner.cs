using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class AlwaysEncryptedComparisonMismatchScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<AlwaysEncryptedComparisonMismatchFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<AlwaysEncryptedComparisonMismatchFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<AlwaysEncryptedComparisonMismatchFinding> Findings { get; } = [];

        public void OnEnterBooleanComparisonExpressionScope(BooleanComparisonExpression node, ModuleWalker walker)
        {
            if (!TryClassify(node.ComparisonType, out var isEquality))
            {
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            Inspect(node.FirstExpression, node.SecondExpression, isEquality, scopeChain, node);
        }

        public void OnBooleanTernaryExpression(BooleanTernaryExpression node, ModuleWalker walker)
        {
            if (node.TernaryExpressionType is not (BooleanTernaryExpressionType.Between or BooleanTernaryExpressionType.NotBetween))
            {
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            Inspect(node.FirstExpression, node.SecondExpression, isEquality: false, scopeChain, node);
            Inspect(node.FirstExpression, node.ThirdExpression, isEquality: false, scopeChain, node);
        }

        private static bool TryClassify(BooleanComparisonType comparisonType, out bool isEquality)
        {
            switch (comparisonType)
            {
                case BooleanComparisonType.Equals:
                case BooleanComparisonType.NotEqualToBrackets:
                case BooleanComparisonType.NotEqualToExclamation:
                case BooleanComparisonType.IsDistinctFrom:
                case BooleanComparisonType.IsNotDistinctFrom:
                    isEquality = true;
                    return true;

                case BooleanComparisonType.GreaterThan:
                case BooleanComparisonType.LessThan:
                case BooleanComparisonType.GreaterThanOrEqualTo:
                case BooleanComparisonType.LessThanOrEqualTo:
                case BooleanComparisonType.NotLessThan:
                case BooleanComparisonType.NotGreaterThan:
                    isEquality = false;
                    return true;

                default:
                    isEquality = false;
                    return false;
            }
        }

        private void Inspect(ScalarExpression first, ScalarExpression second, bool isEquality, ScopeChain scopeChain, TSqlFragment location)
        {
            var firstResolved = ResolveColumn(first, scopeChain);
            var secondResolved = ResolveColumn(second, scopeChain);

            if (firstResolved is { } fc && secondResolved is { } sc)
            {
                InspectColumnPair(fc, sc, isEquality, location);
                return;
            }

            if (firstResolved is { Column.EncryptionType: not Catalog.ColumnEncryptionType.None } encryptedFirst && IsNonNullLiteral(second))
            {
                AddLiteralFinding(encryptedFirst, location);
            }
            else if (secondResolved is { Column.EncryptionType: not Catalog.ColumnEncryptionType.None } encryptedSecond && IsNonNullLiteral(first))
            {
                AddLiteralFinding(encryptedSecond, location);
            }
        }

        private void InspectColumnPair(
            (ColumnProvenance.BaseColumn Reference, CatalogColumn Column) fc,
            (ColumnProvenance.BaseColumn Reference, CatalogColumn Column) sc,
            bool isEquality,
            TSqlFragment location)
        {
            if (fc.Column.EncryptionType == Catalog.ColumnEncryptionType.None && sc.Column.EncryptionType == Catalog.ColumnEncryptionType.None)
            {
                return;
            }

            if (AlwaysEncryptedCompatibility.IsMismatch(fc.Column, sc.Column, catalog.IdentifierComparer))
            {
                Findings.Add(new AlwaysEncryptedComparisonMismatchFinding(
                    AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch,
                    fc.Reference.TableQualifiedName,
                    fc.Reference.ColumnName,
                    AlwaysEncryptedCompatibility.FormatDisplay(fc.Column),
                    sc.Reference.TableQualifiedName,
                    sc.Reference.ColumnName,
                    AlwaysEncryptedCompatibility.FormatDisplay(sc.Column),
                    sourcePath,
                    location.StartLine,
                    location.StartColumn));
                return;
            }

            if (!isEquality && fc.Column.EncryptionType == Catalog.ColumnEncryptionType.Deterministic)
            {
                AddSameProfileFinding(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison, fc, sc, location);
                return;
            }

            if (fc.Column.EncryptionType == Catalog.ColumnEncryptionType.Randomized
                && fc.Column.EnclaveSupport == ColumnEncryptionEnclaveSupport.Disabled)
            {
                AddSameProfileFinding(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave, fc, sc, location);
            }
        }

        private void AddSameProfileFinding(
            AlwaysEncryptedComparisonMismatchKind kind,
            (ColumnProvenance.BaseColumn Reference, CatalogColumn Column) fc,
            (ColumnProvenance.BaseColumn Reference, CatalogColumn Column) sc,
            TSqlFragment location) =>
            Findings.Add(new AlwaysEncryptedComparisonMismatchFinding(
                kind,
                fc.Reference.TableQualifiedName,
                fc.Reference.ColumnName,
                AlwaysEncryptedCompatibility.FormatDisplay(fc.Column),
                sc.Reference.TableQualifiedName,
                sc.Reference.ColumnName,
                AlwaysEncryptedCompatibility.FormatDisplay(sc.Column),
                sourcePath,
                location.StartLine,
                location.StartColumn));

        private void AddLiteralFinding((ColumnProvenance.BaseColumn Reference, CatalogColumn Column) encrypted, TSqlFragment location) =>
            Findings.Add(new AlwaysEncryptedComparisonMismatchFinding(
                AlwaysEncryptedComparisonMismatchKind.LiteralOperand,
                encrypted.Reference.TableQualifiedName,
                encrypted.Reference.ColumnName,
                AlwaysEncryptedCompatibility.FormatDisplay(encrypted.Column),
                SecondTableQualifiedName: null,
                SecondColumnName: null,
                SecondEncryptionTypeDisplay: null,
                sourcePath,
                location.StartLine,
                location.StartColumn));

        private (ColumnProvenance.BaseColumn Reference, CatalogColumn Column)? ResolveColumn(ScalarExpression expression, ScopeChain scopeChain)
        {
            if (expression is not ColumnReferenceExpression columnRef
                || BaseColumnResolver.ResolveBaseColumn(columnRef, sourcePath, scopeChain, catalog) is not { } reference
                || catalog.Find(reference.TableQualifiedName)?.FindColumn(reference.ColumnName, catalog.IdentifierComparer) is not { } column)
            {
                return null;
            }

            return (reference, column);
        }

        private static bool IsNonNullLiteral(ScalarExpression expression) =>
            expression is Literal and not NullLiteral and not DefaultLiteral;
    }
}

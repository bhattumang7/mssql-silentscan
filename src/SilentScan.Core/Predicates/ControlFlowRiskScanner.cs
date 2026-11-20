using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.Normalization;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class ControlFlowRiskScanner
{
    public static IReadOnlyList<ControlFlowRiskFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<ControlFlowRiskFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        private const int MaxCoveredDomainSpan = 1024;

        private readonly Lazy<Dictionary<(string Table, string Column), List<(string ConstraintName, NumericValueRangeSet Domain)>>> _checkDomains =
            new(() => CheckConstraintPredicateContradictionScanner.Rule.BuildDomains(catalog));

        public List<ControlFlowRiskFinding> Findings { get; } = [];

        private static string CurrentModule(ModuleWalker walker) => walker.CurrentProcScope ?? "(batch)";

        private static readonly HashSet<string> NonDeterministicFunctionNames =
            new(StringComparer.OrdinalIgnoreCase) { "NEWID", "RAND", "CRYPT_GEN_RANDOM" };

        public void OnEnterTableHint(TableHint node, ModuleWalker walker)
        {
            if (node.HintKind is TableHintKind.NoLock or TableHintKind.ReadUncommitted)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.DirtyReadIsolationHint,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "NOLOCK/READUNCOMMITTED allows dirty reads of uncommitted, possibly-rolled-back data, and can miss or double-count rows during a concurrent page split.",
                    FindingConfidence.Low));
            }
            else if (node.HintKind == TableHintKind.ReadCommittedLock && catalog.IsReadCommittedSnapshotOn == true)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.ReadCommittedLockRevertsRowVersioning,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "This database has READ_COMMITTED_SNAPSHOT ON, so READ COMMITTED reads are normally row-versioned and non-blocking - the READCOMMITTEDLOCK hint reverts just this one table reference back to blocking/locking reads, silently changing its concurrency behavior relative to the rest of the batch.",
                    FindingConfidence.High));
            }
        }

        public void OnEnterSetTransactionIsolationLevelStatement(SetTransactionIsolationLevelStatement node, ModuleWalker walker)
        {
            if (node.Level == IsolationLevel.ReadUncommitted)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.DirtyReadIsolationHint,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED allows dirty reads for the rest of the session.",
                    FindingConfidence.Low));
            }
        }

        public void OnEnterGlobalVariableExpression(GlobalVariableExpression node, ModuleWalker walker)
        {
            if (string.Equals(node.Name, "@@IDENTITY", StringComparison.OrdinalIgnoreCase))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.LegacyIdentityIntrinsic,
                    CurrentModule(walker), sourcePath, node.StartLine, node.StartColumn,
                    "@@IDENTITY returns the last identity value inserted in this SESSION across ANY table/scope, including one inserted by a trigger fired as a side effect - prefer SCOPE_IDENTITY() unless that broader semantics is specifically wanted.",
                    FindingConfidence.Medium));
            }
        }

        public void OnEnterOperandPosition(TSqlFragment node, ModuleWalker walker)
        {
            if (node is not SimpleCaseExpression simpleCase)
            {
                return;
            }

            if (simpleCase.ElseExpression is null && !WhenValuesCoverInputDomain(simpleCase, walker))
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.CaseExpressionMissingElse,
                    CurrentModule(walker), sourcePath, simpleCase.StartLine, simpleCase.StartColumn,
                    "This simple CASE has no ELSE - an input value matching none of the WHEN values " +
                    "silently evaluates to NULL, no error raised.",
                    FindingConfidence.High));
            }

            if (ContainsNonDeterministicCall(simpleCase.InputExpression) is { } functionName)
            {
                Findings.Add(new ControlFlowRiskFinding(
                    ControlFlowRiskFindingKind.NonDeterministicCaseInput,
                    CurrentModule(walker), sourcePath, simpleCase.StartLine, simpleCase.StartColumn,
                    $"{functionName}() as a CASE input is re-evaluated separately for each WHEN " +
                    "comparison, not once - every WHEN branch is effectively unreachable and this " +
                    "always falls through to ELSE (or NULL, with no ELSE).",
                    FindingConfidence.High));
            }
        }


        private bool WhenValuesCoverInputDomain(SimpleCaseExpression simpleCase, ModuleWalker walker)
        {
            var whenValues = new HashSet<decimal>();
            foreach (var clause in simpleCase.WhenClauses)
            {
                if (clause.WhenExpression is not IntegerLiteral literal || !decimal.TryParse(literal.Value, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }

                whenValues.Add(value);
            }

            if (TryGetFunctionDomain(simpleCase.InputExpression) is { } functionDomain)
            {
                return CoversRange(whenValues, functionDomain.Min, functionDomain.Max);
            }

            if (simpleCase.InputExpression is not ColumnReferenceExpression columnRef
                || walker.ResolveCatalogColumn(columnRef, walker.CurrentScopeChain()) is not { } resolved)
            {
                return false;
            }

            if (resolved.Column.Type?.Category == SqlTypeCategory.Bit)
            {
                return CoversRange(whenValues, 0, 1);
            }

            return resolved.Column.Type?.Category is SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt or SqlTypeCategory.Int or SqlTypeCategory.BigInt
                && _checkDomains.Value.TryGetValue((resolved.TableQualifiedName, resolved.Column.Name), out var domains)
                && domains.Any(d => d.Domain.EveryIntegerIsIn(whenValues, MaxCoveredDomainSpan));
        }

        private static bool CoversRange(HashSet<decimal> values, int min, int max) =>
            Enumerable.Range(min, max - min + 1).All(v => values.Contains(v));

        private static (int Min, int Max)? TryGetFunctionDomain(ScalarExpression input)
        {
            if (input is not FunctionCall { FunctionName.Value: { } name } call)
            {
                return null;
            }

            if (name.Equals("MONTH", StringComparison.OrdinalIgnoreCase) && call.Parameters.Count == 1)
            {
                return (1, 12);
            }

            if (!name.Equals("DATEPART", StringComparison.OrdinalIgnoreCase)
                || call.Parameters is not [IdentifierLiteral part, _])
            {
                return null;
            }

            return part.Value.ToUpperInvariant() switch
            {
                "WEEKDAY" or "DW" or "W" => (1, 7),
                "MONTH" or "MM" or "M" => (1, 12),
                "QUARTER" or "QQ" or "Q" => (1, 4),
                "HOUR" or "HH" => (0, 23),
                _ => null,
            };
        }

        private static string? ContainsNonDeterministicCall(ScalarExpression expression)
        {
            var finder = new NonDeterministicCallFinder();
            expression.Accept(finder);
            return finder.FoundFunctionName;
        }

        private sealed class NonDeterministicCallFinder : TSqlFragmentVisitor
        {
            public string? FoundFunctionName { get; private set; }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (FoundFunctionName is null
                    && node.FunctionName?.Value is { } name
                    && NonDeterministicFunctionNames.Contains(name))
                {
                    FoundFunctionName = name.ToUpperInvariant();
                }

                base.ExplicitVisit(node);
            }
        }
    }
}

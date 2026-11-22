using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class IndexDesignScanner
{
    private const string UnnamedIndexPlaceholder = "<unnamed>";

    public static IReadOnlyList<IndexDesignFinding> Scan(DatabaseCatalog catalog, IScanStage? stage = null)
    {
        var defaultTextByColumn = new Dictionary<string, string>(catalog.IdentifierComparer);
        foreach (var expression in catalog.SchemaExpressions)
        {
            if (expression.Kind != SchemaDependencyKind.DefaultConstraint || expression.ColumnName is null)
            {
                continue;
            }

            var key = DefaultKey(expression.TableQualifiedName, expression.ColumnName);
            defaultTextByColumn.TryAdd(key, expression.DefinitionText);
        }

        var findings = new List<IndexDesignFinding>();

        foreach (var table in catalog.Tables)
        {
            stage?.Advance(currentItem: table.QualifiedName);

            if (table.Kind != CatalogTableKind.Table)
            {
                continue;
            }

            ScanHeapFindings(table, findings);
            ScanClusteringKeyQuality(table, defaultTextByColumn, catalog.IdentifierComparer, findings);
            ScanDuplicateAndSubsumedIndexes(table, catalog.IdentifierComparer, findings);
            ScanFilteredIndexColumnCoverage(table, catalog.CompatibilityLevel, catalog.IdentifierComparer, findings);
            ScanFloatOrRealIndexKeyColumns(table, catalog.IdentifierComparer, findings);
            ScanNoRecomputeStatistics(table, findings);
            ScanMonotonicClusteredKeyMissingSequentialOptimization(table, catalog.IdentifierComparer, findings);
            ScanRowOrPageLockingDisabled(table, findings);
        }

        ScanUnindexedForeignKeys(catalog, findings);

        return
        [
            .. findings
                .OrderBy(f => f.TableQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.Kind)
                .ThenBy(f => f.IndexName, StringComparer.Ordinal),
        ];
    }

    private static void ScanHeapFindings(CatalogTable table, List<IndexDesignFinding> findings)
    {

        if (table.IsMemoryOptimized)
        {
            return;
        }

        var hasClusteredIndex = table.Indexes.Any(i => i.IsClustered);
        if (hasClusteredIndex)
        {
            return;
        }

        var activeNonclustered = table.Indexes.Where(i => !i.IsDisabled).ToList();
        if (activeNonclustered.Count == 0)
        {

            return;
        }

        var names = string.Join(", ", activeNonclustered.Select(i => i.Name ?? UnnamedIndexPlaceholder));
        findings.Add(new IndexDesignFinding(
            IndexDesignFindingKind.HeapWithNonclusteredIndexes,
            table.QualifiedName,
            activeNonclustered[0].Name,
            $"'{table.QualifiedName}' has no clustered index anywhere but carries {activeNonclustered.Count} nonclustered index(es) ({names}) - each one points back to its base row with an 8-byte RID instead of the clustering key, and heap maintenance leaves forwarded rows that add a hop to every lookup of them, while a heap rebuild moves rows to new RIDs.",
            table.SourcePath,
            table.SourceLine));
    }

    private static void ScanClusteringKeyQuality(
        CatalogTable table, Dictionary<string, string> defaultTextByColumn, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {

        var clusteredIndex = table.Indexes.FirstOrDefault(i => i.IsClustered && !i.IsColumnstore);
        if (clusteredIndex is null || clusteredIndex.KeyColumns.Count == 0)
        {
            return;
        }

        CheckRandomClusteredKeyGuidDefault(table, clusteredIndex, defaultTextByColumn, identifierComparer, findings);
    }

    private static void CheckRandomClusteredKeyGuidDefault(
        CatalogTable table, CatalogIndex clusteredIndex, Dictionary<string, string> defaultTextByColumn, StringComparer identifierComparer,
        List<IndexDesignFinding> findings)
    {
        var leadingColumnName = clusteredIndex.KeyColumns[0];
        var leadingColumn = table.FindColumn(leadingColumnName, identifierComparer);
        if (leadingColumn?.Type?.Category != SqlTypeCategory.UniqueIdentifier)
        {
            return;
        }

        var key = DefaultKey(table.QualifiedName, leadingColumnName);
        if (!defaultTextByColumn.TryGetValue(key, out var defaultText) || !IsNewIdDefault(defaultText))
        {
            return;
        }

        findings.Add(new IndexDesignFinding(
            IndexDesignFindingKind.RandomClusteredKeyGuidDefault,
            table.QualifiedName,
            clusteredIndex.Name,
            $"'{table.QualifiedName}' clustered index '{clusteredIndex.Name ?? UnnamedIndexPlaceholder}' leads on '{leadingColumnName}', a uniqueidentifier column defaulted to NEWID() - genuinely random insert order into a clustered B-tree causes severe page splits and fragmentation. NEWSEQUENTIALID() avoids this and does not fire here.",
            table.SourcePath,
            table.SourceLine));
    }

    private static void ScanDuplicateAndSubsumedIndexes(CatalogTable table, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {
        var candidates = table.Indexes
            .Where(i => !i.IsDisabled && !i.IsFiltered && !i.IsColumnstore && !i.IsJsonIndex && !i.IsXmlIndex && !i.IsSpatialIndex && !i.IsHypothetical && i.KeyColumns.Count > 0)
            .ToList();

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                CompareIndexPairForDuplicateOrSubsumed(table, candidates[i], candidates[j], identifierComparer, findings);
            }
        }
    }

    private static void CompareIndexPairForDuplicateOrSubsumed(
        CatalogTable table, CatalogIndex a, CatalogIndex b, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {
        if (a.IsUnique != b.IsUnique || a.Kind != b.Kind || a.IsClustered != b.IsClustered
            || !string.Equals(a.PartitionSchemeName, b.PartitionSchemeName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (a.KeyColumns.Count == b.KeyColumns.Count)
        {
            if (KeyColumnsEqual(a.KeyColumns, b.KeyColumns, identifierComparer)
                && IncludedColumnsEqual(a.IncludedColumns, b.IncludedColumns, identifierComparer)
                && SortDirectionsEqual(a, b))
            {
                findings.Add(new IndexDesignFinding(
                    IndexDesignFindingKind.DuplicateIndex,
                    table.QualifiedName,
                    b.Name,
                    $"'{table.QualifiedName}' indexes '{a.Name ?? UnnamedIndexPlaceholder}' and '{b.Name ?? UnnamedIndexPlaceholder}' share the identical key list ({string.Join(", ", a.KeyColumns)}) with the same sort directions, the same included columns, the same uniqueness, and the same index kind - exact duplicates. One is pure write amplification and wasted space with zero query benefit over the other.",
                    table.SourcePath,
                    table.SourceLine));
            }

            return;
        }

        var (shorter, longer) = a.KeyColumns.Count < b.KeyColumns.Count ? (a, b) : (b, a);
        if (!shorter.IsUnique
            && IsProperPrefix(shorter.KeyColumns, longer.KeyColumns, identifierComparer)
            && PrefixSortDirectionsEqual(shorter, longer)
            && shorter.IncludedColumns.All(c => longer.IncludedColumns.Contains(c, identifierComparer)))
        {
            findings.Add(new IndexDesignFinding(
                IndexDesignFindingKind.SubsumedIndex,
                table.QualifiedName,
                shorter.Name,
                $"'{table.QualifiedName}' index '{shorter.Name ?? UnnamedIndexPlaceholder}' ({string.Join(", ", shorter.KeyColumns)}) is a leading-column prefix of '{longer.Name ?? UnnamedIndexPlaceholder}' ({string.Join(", ", longer.KeyColumns)}), with its own INCLUDE columns already covered by '{longer.Name ?? UnnamedIndexPlaceholder}' - '{shorter.Name ?? UnnamedIndexPlaceholder}' is redundant, since '{longer.Name ?? UnnamedIndexPlaceholder}' can serve every seek it could.",
                table.SourcePath,
                table.SourceLine));
        }
    }

    private static bool PrefixSortDirectionsEqual(CatalogIndex shorter, CatalogIndex longer)
    {
        for (var k = 0; k < shorter.KeyColumns.Count; k++)
        {
            var shorterDescending = k < shorter.KeyColumnIsDescending.Count && shorter.KeyColumnIsDescending[k];
            var longerDescending = k < longer.KeyColumnIsDescending.Count && longer.KeyColumnIsDescending[k];
            if (shorterDescending != longerDescending)
            {
                return false;
            }
        }

        return true;
    }

    private static bool KeyColumnsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b, StringComparer identifierComparer) =>
        a.SequenceEqual(b, identifierComparer);

    private static bool IncludedColumnsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b, StringComparer identifierComparer) =>
        a.Count == b.Count && a.All(c => b.Contains(c, identifierComparer));

    private static bool SortDirectionsEqual(CatalogIndex a, CatalogIndex b)
    {
        for (var k = 0; k < a.KeyColumns.Count; k++)
        {
            var aDescending = k < a.KeyColumnIsDescending.Count && a.KeyColumnIsDescending[k];
            var bDescending = k < b.KeyColumnIsDescending.Count && b.KeyColumnIsDescending[k];
            if (aDescending != bDescending)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsProperPrefix(IReadOnlyList<string> shorter, IReadOnlyList<string> longer, StringComparer identifierComparer)
    {
        if (shorter.Count == 0 || shorter.Count >= longer.Count)
        {
            return false;
        }

        for (var k = 0; k < shorter.Count; k++)
        {
            if (!identifierComparer.Equals(shorter[k], longer[k]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ScanUnindexedForeignKeys(DatabaseCatalog catalog, List<IndexDesignFinding> findings)
    {
        var constraints = catalog.ForeignKeys
            .GroupBy(fk => (fk.ConstraintName, fk.ParentTableQualifiedName), fk => fk, new TupleComparer(catalog.IdentifierComparer));

        foreach (var group in constraints)
        {
            var parentTable = catalog.Find(group.Key.ParentTableQualifiedName);
            if (parentTable is null || group.Any(fk => fk.IsDisabled))
            {

                continue;
            }

            var fkColumns = new HashSet<string>(group.Select(fk => fk.ParentColumnName), catalog.IdentifierComparer);
            var referencedTable = group.First().ReferencedTableQualifiedName;

            var hasLeadingIndex = parentTable.Indexes.Any(i =>
                !i.IsDisabled && !i.IsFiltered && !i.IsColumnstore && !i.IsJsonIndex
                && i.KeyColumns.Count >= fkColumns.Count
                && i.KeyColumns.Take(fkColumns.Count).All(c => fkColumns.Contains(c)));

            if (hasLeadingIndex)
            {
                continue;
            }

            findings.Add(new IndexDesignFinding(
                IndexDesignFindingKind.UnindexedForeignKey,
                parentTable.QualifiedName,
                IndexName: null,
                $"'{parentTable.QualifiedName}' foreign key '{group.Key.ConstraintName}' ({string.Join(", ", fkColumns)}) referencing '{referencedTable}' has no active index leading on its own column set - every parent-side DELETE/UPDATE forces a referential-integrity scan of this table, and every join along this relationship has no seek path.",
                parentTable.SourcePath,
                parentTable.SourceLine));
        }
    }

    private sealed class TupleComparer(StringComparer identifierComparer) : IEqualityComparer<(string ConstraintName, string ParentTableQualifiedName)>
    {
        public bool Equals((string ConstraintName, string ParentTableQualifiedName) x, (string ConstraintName, string ParentTableQualifiedName) y) =>
            identifierComparer.Equals(x.ConstraintName, y.ConstraintName)
            && identifierComparer.Equals(x.ParentTableQualifiedName, y.ParentTableQualifiedName);

        public int GetHashCode((string ConstraintName, string ParentTableQualifiedName) obj) =>
            HashCode.Combine(
                identifierComparer.GetHashCode(obj.ConstraintName),
                identifierComparer.GetHashCode(obj.ParentTableQualifiedName));
    }

    private static void ScanFilteredIndexColumnCoverage(
        CatalogTable table, int? compatibilityLevel, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {
        foreach (var index in table.Indexes)
        {
            if (!index.IsFiltered || index.FilterDefinition is not { } filterDefinition)
            {
                continue;
            }

            var filterColumns = TryExtractFilterColumnNames(filterDefinition, compatibilityLevel);
            if (filterColumns is null || filterColumns.Count == 0)
            {
                continue;
            }

            var carriedColumns = new HashSet<string>(index.KeyColumns, identifierComparer);
            carriedColumns.UnionWith(index.IncludedColumns);

            var missing = filterColumns.Where(c => !carriedColumns.Contains(c)).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            findings.Add(new IndexDesignFinding(
                IndexDesignFindingKind.FilterColumnNotInIndex,
                table.QualifiedName,
                index.Name,
                $"'{table.QualifiedName}' filtered index '{index.Name ?? UnnamedIndexPlaceholder}' has filter '{filterDefinition}' referencing column(s) not in its own key/INCLUDE list: {string.Join(", ", missing)} - the engine can only substitute this index for a query whose own WHERE clause restates the filter predicate, and a query that returns or otherwise needs those column(s) pays a key lookup per row to read them from the base table.",
                table.SourcePath,
                table.SourceLine));
        }
    }

    private static List<string>? TryExtractFilterColumnNames(string filterDefinition, int? compatibilityLevel)
    {
        var result = SqlScriptParser.ParseText("filter-definition.sql", $"SELECT 1 WHERE {filterDefinition};", initialQuotedIdentifiers: true, compatibilityLevel);
        if (result.HasErrors
            || result.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { WhereClause.SearchCondition: { } searchCondition } } ] }] })
        {
            return null;
        }

        var collector = new ColumnNameCollector();
        searchCondition.Accept(collector);
        return [.. collector.Names];
    }

    public static (string ColumnName, string LiteralText)? TryExtractSimpleLiteralEqualityFilter(string filterDefinition, int? compatibilityLevel = null)
    {
        var result = SqlScriptParser.ParseText("filter-definition.sql", $"SELECT 1 WHERE {filterDefinition};", initialQuotedIdentifiers: true, compatibilityLevel);
        if (result.HasErrors
            || result.Fragment is not TSqlScript { Batches: [{ Statements: [SelectStatement { QueryExpression: QuerySpecification { WhereClause.SearchCondition: { } searchCondition } }] }] })
        {
            return null;
        }

        var condition = searchCondition;
        while (condition is BooleanParenthesisExpression parenthesized)
        {
            condition = parenthesized.Expression;
        }

        if (condition is not BooleanComparisonExpression
            {
                ComparisonType: BooleanComparisonType.Equals,
                FirstExpression: ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { Value: { } columnName }] },
                SecondExpression: Literal literal,
            }
            || Rules.LiteralTextRenderer.Render(literal) is not { } literalText)
        {
            return null;
        }

        return (columnName, literalText);
    }

    private sealed class ColumnNameCollector : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier?.Identifiers is { Count: > 0 } identifiers)
            {
                Names.Add(identifiers[^1].Value);
            }

            base.ExplicitVisit(node);
        }
    }

    private static void ScanFloatOrRealIndexKeyColumns(CatalogTable table, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {
        foreach (var index in table.Indexes)
        {
            if (index.IsDisabled || index.KeyColumns.Count == 0)
            {
                continue;
            }

            var floatKeyColumns = index.KeyColumns
                .Where(name => table.FindColumn(name, identifierComparer)?.Type?.Category is SqlTypeCategory.Real or SqlTypeCategory.Float)
                .ToList();

            if (floatKeyColumns.Count == 0)
            {
                continue;
            }

            findings.Add(new IndexDesignFinding(
                IndexDesignFindingKind.FloatOrRealIndexKeyColumn,
                table.QualifiedName,
                index.Name,
                $"'{table.QualifiedName}' index '{index.Name ?? UnnamedIndexPlaceholder}' carries approximate (float/real) key column(s) {string.Join(", ", floatKeyColumns)} - IEEE-754 binary floating-point cannot represent every decimal value exactly, so an equality seek/comparison against it can silently miss a value a person would call 'the same number'.",
                table.SourcePath,
                table.SourceLine));
        }
    }

    private static void ScanNoRecomputeStatistics(CatalogTable table, List<IndexDesignFinding> findings) =>
        findings.AddRange(table.EffectiveStatistics.Where(s => s.NoRecompute).Select(stat => new IndexDesignFinding(
            IndexDesignFindingKind.NoRecomputeStatistics,
            table.QualifiedName,
            stat.Name,
            $"'{table.QualifiedName}' statistics object '{stat.Name}' is marked NORECOMPUTE - the engine's automatic statistics maintenance never refreshes it, so its cardinality estimate silently drifts stale as the table's data changes.",
            table.SourcePath,
            table.SourceLine,
            Confidence: FindingConfidence.Medium)));

    private static void ScanMonotonicClusteredKeyMissingSequentialOptimization(CatalogTable table, StringComparer identifierComparer, List<IndexDesignFinding> findings)
    {
        var clusteredIndex = table.Indexes.FirstOrDefault(i => i.IsClustered && !i.IsColumnstore && !i.IsDisabled);
        if (clusteredIndex is null || clusteredIndex.KeyColumns.Count == 0 || clusteredIndex.OptimizeForSequentialKey)
        {
            return;
        }

        var leadingColumn = table.FindColumn(clusteredIndex.KeyColumns[0], identifierComparer);
        if (leadingColumn is not { IsIdentity: true, IdentityIncrement: > 0 })
        {
            return;
        }

        findings.Add(new IndexDesignFinding(
            IndexDesignFindingKind.MonotonicClusteredKeyMissingSequentialOptimization,
            table.QualifiedName,
            clusteredIndex.Name,
            $"'{table.QualifiedName}' clustered index '{clusteredIndex.Name ?? UnnamedIndexPlaceholder}' leads on '{leadingColumn.Name}', an always-ascending IDENTITY column, with OPTIMIZE_FOR_SEQUENTIAL_KEY not enabled - every insert lands on the same trailing page, so concurrent inserts can serialize on that page's latch. Structural risk flag only: whether this actually causes contention depends on concurrent insert rate, which is workload data out of reach for this static pass.",
            table.SourcePath,
            table.SourceLine,
            FindingConfidence.Medium));
    }

    private static void ScanRowOrPageLockingDisabled(CatalogTable table, List<IndexDesignFinding> findings)
    {
        foreach (var index in table.Indexes)
        {
            if (index.IsDisabled || index.IsHypothetical || index.IsColumnstore)
            {
                continue;
            }

            if (index.AllowRowLocks && index.AllowPageLocks)
            {
                continue;
            }

            var disabledOptions = new List<string>();
            if (!index.AllowRowLocks)
            {
                disabledOptions.Add("ALLOW_ROW_LOCKS");
            }

            if (!index.AllowPageLocks)
            {
                disabledOptions.Add("ALLOW_PAGE_LOCKS");
            }

            findings.Add(new IndexDesignFinding(
                IndexDesignFindingKind.RowOrPageLockingDisabled,
                table.QualifiedName,
                index.Name,
                $"'{table.QualifiedName}' index '{index.Name ?? UnnamedIndexPlaceholder}' has {string.Join(" and ", disabledOptions)} set to OFF - any DML statement touching this index is silently forced onto a coarser locking granularity than the statement itself suggests, with no hint of this at the DML site. Structural risk flag only: whether this actually causes blocking or deadlocks depends on the concurrent access pattern, which is workload data out of reach for this static pass.",
                table.SourcePath,
                table.SourceLine,
                FindingConfidence.Medium));
        }
    }

    private static bool IsNewIdDefault(string? definitionText)
    {
        if (definitionText is null)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[definitionText.Length];
        var length = 0;
        foreach (var c in definitionText)
        {
            if (c is '(' or ')' || char.IsWhiteSpace(c))
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(c);
        }

        return buffer[..length].SequenceEqual("newid");
    }

    private static string DefaultKey(string tableQualifiedName, string columnName) =>
        $"{tableQualifiedName}\x01{columnName}";
}

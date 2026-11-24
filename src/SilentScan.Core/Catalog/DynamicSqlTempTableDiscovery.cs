using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.DynamicSqlValue;
using SilentScan.Core.Common;

namespace SilentScan.Core.Catalog;

public static class DynamicSqlTempTableDiscovery
{
    private const string CreateTableMarker = "CREATE TABLE";

    private const string SelectIntoMarker = "INTO";

    public static DatabaseCatalog Discover(
        IEnumerable<SqlParseResult> parseResults, string? manifestDeclaredCollation = null, string? manifestTempdbCollation = null, int? compatibilityLevel = null,
        DatabaseCatalog? enclosingCatalog = null, IScanStage? stage = null)
    {
        var wrapped = new List<SqlParseResult>();
        foreach (var result in parseResults)
        {
            stage?.Advance(currentItem: result.SourcePath);

            if (result.HasErrors)
            {
                continue;
            }

            Dictionary<TSqlStatement, bool?>? ansiNullDfltStates = null;

            foreach (var script in DynamicSqlScannerV2.Scan(result).AnalyzableScripts)
            {
                if (script.Scope.ProcScope is not { Length: > 0 } scope || !MayDefineTempTable(script.InnerText))
                {
                    continue;
                }

                var initialQuotedIdentifiers = enclosingCatalog?.ResolveDynamicSqlQuotedIdentifier(scope) ?? true;
                ansiNullDfltStates ??= AnsiNullDfltFlowResolver.Resolve(result.Fragment);
                var ansiNullDfltOverride = ResolveAnsiNullDfltOverrideAt(ansiNullDfltStates, script.CallSite.Line, script.CallSite.Column);
                if (TryWrapAsScopedProcedure(script.InnerText, scope, result.SourcePath, initialQuotedIdentifiers, compatibilityLevel, ansiNullDfltOverride) is { } wrappedResult)
                {
                    wrapped.Add(wrappedResult);
                }
            }
        }

        return wrapped.Count == 0 ? new DatabaseCatalog() : CatalogBuilder.Build(wrapped, manifestDeclaredCollation, manifestTempdbCollation, enclosingCatalog?.IsAnsiNullDefaultOn);
    }

    public static DatabaseCatalog? BuildScriptCatalog(
        DatabaseCatalog catalog, Lineage.LineageCatalog lineage, string scope, string innerText, SqlParseResult innerParseResult)
    {
        if (!MayDefineTempTable(innerText) || WrapAsScopedProcedure(innerParseResult, scope) is not { } wrapped)
        {
            return null;
        }

        var permanentTables = catalog.Tables.Where(t => t.Kind is CatalogTableKind.Table or CatalogTableKind.TableType).ToList();
        var local = CatalogBuilder.Build(
            [wrapped], catalog.DefaultCollation?.Name, catalog.TempdbCollation?.Name, catalog.IsAnsiNullDefaultOn, knownTables: permanentTables);
        var defined = local.Tables.Where(t => t.Kind == CatalogTableKind.TemporaryTable && t.SchemaName is null).ToList();
        if (defined.Count == 0)
        {
            return null;
        }

        var scoped = catalog.WithScriptTables(scope, defined.Select(t => local.Find(t.QualifiedName, scope) ?? t));
        Lineage.SelectIntoLineagePass.Apply(scoped, lineage, [wrapped]);
        return scoped;
    }

    private static SqlParseResult? WrapAsScopedProcedure(SqlParseResult inner, string scope)
    {
        var dotIndex = scope.IndexOf('.', StringComparison.Ordinal);
        if (inner.Fragment is not TSqlScript script || dotIndex <= 0 || dotIndex == scope.Length - 1)
        {
            return null;
        }

        var name = new SchemaObjectName();
        name.Identifiers.Add(new Identifier { Value = scope[..dotIndex] });
        name.Identifiers.Add(new Identifier { Value = scope[(dotIndex + 1)..] });
        var procedure = new CreateProcedureStatement { ProcedureReference = new ProcedureReference { Name = name }, StatementList = new StatementList() };
        foreach (var statement in script.Batches.SelectMany(batch => batch.Statements))
        {
            procedure.StatementList.Statements.Add(statement);
        }

        var wrappedBatch = new TSqlBatch();
        wrappedBatch.Statements.Add(procedure);
        var wrappedScript = new TSqlScript();
        wrappedScript.Batches.Add(wrappedBatch);
        return new SqlParseResult(inner.SourcePath, wrappedScript, []);
    }

    private static bool MayDefineTempTable(string innerText) =>
        innerText.Contains(CreateTableMarker, StringComparison.OrdinalIgnoreCase)
        || innerText.Contains(SelectIntoMarker, StringComparison.OrdinalIgnoreCase);

    private static SqlParseResult? TryWrapAsScopedProcedure(
        string innerText, string scope, string sourcePath, bool initialQuotedIdentifiers, int? compatibilityLevel, bool? ansiNullDfltOverride)
    {
        var dotIndex = scope.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= 0 || dotIndex == scope.Length - 1)
        {
            return null;
        }

        var schema = Bracket(scope[..dotIndex]);
        var name = Bracket(scope[(dotIndex + 1)..]);
        var ansiNullDfltPrefix = ansiNullDfltOverride switch
        {
            true => "SET ANSI_NULL_DFLT_ON ON; ",
            false => "SET ANSI_NULL_DFLT_OFF ON; ",
            null => string.Empty,
        };
        var wrapperSql = $"CREATE PROCEDURE [{schema}].[{name}] AS BEGIN {ansiNullDfltPrefix}{innerText} END";
        var parsed = SqlScriptParser.ParseText(sourcePath, wrapperSql, initialQuotedIdentifiers, compatibilityLevel);
        return parsed.HasErrors ? null : parsed;
    }

    private static string Bracket(string identifier) => identifier.Replace("]", "]]", StringComparison.Ordinal);

    private static bool? ResolveAnsiNullDfltOverrideAt(Dictionary<TSqlStatement, bool?> states, int line, int column)
    {
        bool? result = null;
        var bestLine = -1;
        var bestColumn = -1;
        foreach (var (statement, value) in states)
        {
            if (statement.StartLine > line || (statement.StartLine == line && statement.StartColumn > column))
            {
                continue;
            }

            if (statement.StartLine < bestLine || (statement.StartLine == bestLine && statement.StartColumn < bestColumn))
            {
                continue;
            }

            bestLine = statement.StartLine;
            bestColumn = statement.StartColumn;
            result = value;
        }

        return result;
    }
}

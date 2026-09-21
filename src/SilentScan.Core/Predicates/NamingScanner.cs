using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class NamingScanner
{
    public static IReadOnlyList<NamingFinding> Scan(SqlParseResult parseResult, DatabaseCatalog? catalog = null)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, new DatabaseCatalog(), EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);

    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog? catalog = null) => new(sourcePath, catalog?.IdentifierComparer ?? StringComparer.OrdinalIgnoreCase, catalog);


    internal static IReadOnlyList<NamingFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.Kind)
                .ThenBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];


    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    internal sealed class Rule(string sourcePath, StringComparer identifierComparer, DatabaseCatalog? catalog = null) : IModuleRule
    {
        private string? _currentViewModule;

        public List<NamingFinding> Findings { get; } = [];

        private string CurrentModule(ModuleWalker walker) => _currentViewModule ?? walker.CurrentProcScope ?? sourcePath;

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker)
        {
            var (name, kindLabel) = node switch
            {
                CreateProcedureStatement p => (p.ProcedureReference.Name, "procedure"),
                AlterProcedureStatement p => (p.ProcedureReference.Name, "procedure"),
                CreateOrAlterProcedureStatement p => (p.ProcedureReference.Name, "procedure"),
                CreateFunctionStatement f => (f.Name, "function"),
                AlterFunctionStatement f => (f.Name, "function"),
                CreateOrAlterFunctionStatement f => (f.Name, "function"),
                _ => (null, null),
            };

            if (name is null)
            {
                return;
            }

            CheckQualification(name, kindLabel!, walker);
            CheckParameters(node.Parameters, walker);
        }

        public void OnEnterCreateViewStatement(CreateViewStatement node, ModuleWalker walker)
        {
            _currentViewModule = SchemaObjectNameHelper.Qualify(node.SchemaObjectName);
            CheckQualification(node.SchemaObjectName, "view", walker);
        }

        public void OnLeaveCreateViewStatement(CreateViewStatement node, ModuleWalker walker) => _currentViewModule = null;

        public void OnEnterAlterViewStatement(AlterViewStatement node, ModuleWalker walker)
        {
            _currentViewModule = SchemaObjectNameHelper.Qualify(node.SchemaObjectName);
            CheckQualification(node.SchemaObjectName, "view", walker);
        }

        public void OnLeaveAlterViewStatement(AlterViewStatement node, ModuleWalker walker) => _currentViewModule = null;

        public void OnEnterCreateTableStatement(CreateTableStatement node, ModuleWalker walker)
        {
            if (node.Definition is not null)
            {
                foreach (var column in node.Definition.ColumnDefinitions)
                {
                    CheckTypeQualifier(column.DataType, walker);
                }
            }
        }

        public void OnEnterDeclareVariableStatement(DeclareVariableStatement node, ModuleWalker walker)
        {
            foreach (var element in node.Declarations)
            {
                CheckTypeQualifier(element.DataType, walker);
            }
        }

        private void CheckParameters(IList<ProcedureParameter> parameters, ModuleWalker walker)
        {
            foreach (var parameter in parameters)
            {
                CheckTypeQualifier(parameter.DataType, walker);
            }
        }

        private void CheckQualification(SchemaObjectName name, string kindLabel, ModuleWalker walker)
        {
            if (name.SchemaIdentifier is null)
            {
                Findings.Add(new NamingFinding(
                    NamingFindingKind.UnqualifiedCreate, CurrentModule(walker), sourcePath,
                    name.BaseIdentifier.StartLine, name.BaseIdentifier.StartColumn,
                    $"{char.ToUpperInvariant(kindLabel[0])}{kindLabel[1..]} \"{name.BaseIdentifier.Value}\" is created with no explicit schema qualifier - its real owning schema depends on the connecting principal's own default schema."));
            }
        }

        private void CheckTypeQualifier(DataTypeReference? dataType, ModuleWalker walker)
        {
            if (dataType is not UserDataTypeReference { Name.SchemaIdentifier: { } schema } userType)
            {
                return;
            }

            if (!identifierComparer.Equals(schema.Value, SchemaObjectNameHelper.DefaultSchema))
            {
                return;
            }

            if (SameNamedTypeExistsInAnotherSchema(schema.Value, userType.Name.BaseIdentifier.Value))
            {
                return;
            }

            Findings.Add(new NamingFinding(
                NamingFindingKind.RedundantTypeQualifier, CurrentModule(walker), sourcePath,
                userType.StartLine, userType.StartColumn,
                $"Type reference \"{schema.Value}.{userType.Name.BaseIdentifier.Value}\" carries a redundant schema qualifier."));
        }

        private bool SameNamedTypeExistsInAnotherSchema(string schemaName, string baseName)
        {
            if (catalog is null)
            {
                return false;
            }

            foreach (var qualifiedName in catalog.TypeAliases.Keys)
            {
                var separatorIndex = qualifiedName.IndexOf('.');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var otherSchema = qualifiedName[..separatorIndex];
                var otherName = qualifiedName[(separatorIndex + 1)..];
                if (!identifierComparer.Equals(otherSchema, schemaName) && identifierComparer.Equals(otherName, baseName))
                {
                    return true;
                }
            }

            foreach (var table in catalog.Tables)
            {
                if (table.Kind != CatalogTableKind.TableType || table.SchemaName is null)
                {
                    continue;
                }

                if (!identifierComparer.Equals(table.SchemaName, schemaName) && identifierComparer.Equals(table.Name, baseName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

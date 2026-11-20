using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

internal abstract class VariableTrackingModuleRule(DatabaseCatalog catalog) : IModuleRule
{
    protected DatabaseCatalog Catalog { get; } = catalog;

    protected Dictionary<string, SqlType?> VariableTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker) => VariableTypes.Clear();

    public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker) => SeedOwnParameters(walker);

    public void OnLeaveProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker) => VariableTypes.Clear();

    public void OnEnterTriggerBody(TriggerStatementBody node, ModuleWalker walker) => VariableTypes.Clear();

    public void OnLeaveTriggerBody(TriggerStatementBody node, ModuleWalker walker) => VariableTypes.Clear();

    public void OnEnterDeclareVariableStatement(DeclareVariableStatement node, ModuleWalker walker)
    {
        foreach (var declaration in node.Declarations)
        {
            VariableTypes[declaration.VariableName.Value] =
                SqlTypeReferenceResolver.Resolve(declaration.DataType, columnCollation: null, Catalog.TypeAliases);
        }
    }

    private void SeedOwnParameters(ModuleWalker walker)
    {
        VariableTypes.Clear();
        if (walker.CurrentProcScope is { } scope && Catalog.TryGetProcedureParameters(scope, out var ownFormalParameters))
        {
            foreach (var parameter in ownFormalParameters)
            {
                VariableTypes[parameter.Name] = parameter.Type;
            }
        }
    }
}

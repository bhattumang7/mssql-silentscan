using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Live.Sweep;

public static class MetadataProcedureCalls
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_refreshview",
        "sp_settriggerorder",
        "sp_create_plan_guide",
        "sp_control_plan_guide",
    };

    public static bool IsMetadataCall(TSqlStatement statement) =>
        statement is ExecuteStatement { ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: { } name } }
        && Names.Contains(name);
}

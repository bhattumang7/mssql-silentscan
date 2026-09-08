using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Live.Sweep;

public static class ServerScopedStatementGuard
{
    public static bool ContainsServerScopedDdl(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("server-scope-guard.sql", sql);
        if (parseResult.Fragment is not TSqlScript { Batches.Count: > 0 } script)
        {
            return false;
        }

        return script.Batches.SelectMany(b => b.Statements).Any(IsServerScoped);
    }

    private static bool IsServerScoped(TSqlStatement statement) => statement switch
    {
        TriggerStatementBody trigger => trigger.TriggerObject?.TriggerScope == TriggerScope.AllServer,
        CreateLoginStatement or AlterLoginStatement or DropLoginStatement => true,
        CreateServerRoleStatement or AlterServerRoleStatement or DropServerRoleStatement => true,
        CreateServerAuditStatement or AlterServerAuditStatement or DropServerAuditStatement => true,
        CreateServerAuditSpecificationStatement or AlterServerAuditSpecificationStatement or DropServerAuditSpecificationStatement => true,
        CreateCredentialStatement or AlterCredentialStatement or DropCredentialStatement => true,
        CreateEndpointStatement or DropEndpointStatement => true,
        AlterServerConfigurationStatement => true,
        CreateAvailabilityGroupStatement or AlterAvailabilityGroupStatement or DropAvailabilityGroupStatement => true,
        IfStatement ifStatement =>
            (ifStatement.ThenStatement is not null && IsServerScoped(ifStatement.ThenStatement))
            || (ifStatement.ElseStatement is not null && IsServerScoped(ifStatement.ElseStatement)),
        BeginEndBlockStatement beginEnd => beginEnd.StatementList.Statements.Any(IsServerScoped),
        _ => false,
    };
}

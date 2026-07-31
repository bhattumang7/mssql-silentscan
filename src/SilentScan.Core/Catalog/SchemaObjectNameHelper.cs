using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Catalog;

public static class SchemaObjectNameHelper
{
    public const string DefaultSchema = "dbo";

    public static (string? Schema, string Name) Resolve(SchemaObjectName name)
    {
        if (name.BaseIdentifier.Value.StartsWith('#'))
        {
            // Temp tables have no schema.
            return (null, name.BaseIdentifier.Value);
        }

        var schema = name.SchemaIdentifier?.Value ?? DefaultSchema;
        return (schema, name.BaseIdentifier.Value);
    }

    public static string Qualify(SchemaObjectName name)
    {
        var (schema, tableName) = Resolve(name);
        return schema is null ? tableName : $"{schema}.{tableName}";
    }
}

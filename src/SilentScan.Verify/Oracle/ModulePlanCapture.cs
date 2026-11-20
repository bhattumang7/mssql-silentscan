using System.Globalization;
using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

public sealed class ModulePlanCapture
{
    private readonly SqlServerOptions _options;

    public ModulePlanCapture(SqlServerOptions options)
    {
        _options = options;
    }

    public async Task<ModulePlanCaptureResult> CaptureAsync(
        string database, string qualifiedName, PlannableModuleKind kind, CancellationToken cancellationToken = default)
    {
        try
        {
            var bracketed = BracketQualifiedName(qualifiedName);
            var request = await BuildRequestAsync(database, bracketed, kind, cancellationToken);
            if (request.Statement is null)
            {
                return new ModulePlanCaptureResult(null, ModuleNotDecidedReason.ModuleKindNotPlannable, request.Detail);
            }

            var planXml = await new PlanXmlCapture(_options).CaptureAsync(database, request.Statement, cancellationToken);
            return new ModulePlanCaptureResult(planXml, ModuleNotDecidedReason.None, null);
        }
        catch (SqlException ex) when (ex.Class is >= 11 and <= 16)
        {
            return new ModulePlanCaptureResult(null, ModuleNotDecidedReason.ModuleDoesNotCompile, ex.Message);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new ModulePlanCaptureResult(null, ModuleNotDecidedReason.PlanRequestFailed, ex.Message);
        }
    }

    private async Task<(string? Statement, string? Detail)> BuildRequestAsync(
        string database, string bracketedName, PlannableModuleKind kind, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case PlannableModuleKind.View:
                return ($"SELECT * FROM {bracketedName};", null);

            case PlannableModuleKind.InlineTableFunction:
                var types = await new FunctionParameterReader(_options).TryGetParameterTypesAsync(database, bracketedName, cancellationToken);
                if (types is null)
                {
                    return (null, "The function's parameter types could not be rendered.");
                }

                var declarations = new List<string>(types.Count);
                var arguments = new List<string>(types.Count);
                foreach (var type in types)
                {
                    var syntax = SqlTypeSyntaxFormatter.Format(type);
                    if (syntax is null)
                    {
                        return (null, "A function parameter type could not be rendered.");
                    }

                    var variable = string.Create(CultureInfo.InvariantCulture, $"@arg{declarations.Count}");
                    declarations.Add($"{variable} {syntax}");
                    arguments.Add(variable);
                }

                var declare = declarations.Count == 0 ? string.Empty : $"DECLARE {string.Join(", ", declarations)}; ";
                return ($"{declare}SELECT * FROM {bracketedName}({string.Join(", ", arguments)});", null);

            case PlannableModuleKind.Procedure:
                return await BuildProcedureRequestAsync(database, bracketedName, cancellationToken);

            default:
                return (null, string.Create(CultureInfo.InvariantCulture, $"Module kind {kind} is not plannable."));
        }
    }

    private async Task<(string? Statement, string? Detail)> BuildProcedureRequestAsync(
        string database, string bracketedName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.name, ty.is_table_type
            FROM sys.parameters p
            JOIN sys.types ty ON ty.user_type_id = p.user_type_id
            WHERE p.object_id = OBJECT_ID(@objectName)
              AND p.parameter_id > 0
            ORDER BY p.parameter_id;
            """;

        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@objectName", bracketedName);

        var assignments = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(1))
            {
                return (null, "The procedure has a table-valued parameter.");
            }

            assignments.Add($"{reader.GetString(0)} = NULL");
        }

        return (assignments.Count == 0
            ? $"EXEC {bracketedName};"
            : $"EXEC {bracketedName} {string.Join(", ", assignments)};", null);
    }

    private static string BracketQualifiedName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.', 2);
        return parts.Length == 2 ? $"{Bracket(parts[0])}.{Bracket(parts[1])}" : Bracket(parts[0]);
    }

    private static string Bracket(string identifier)
    {
        var bare = identifier.Length > 1 && identifier[0] == '[' && identifier[^1] == ']'
            ? identifier[1..^1].Replace("]]", "]", StringComparison.Ordinal)
            : identifier;
        return $"[{bare.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}

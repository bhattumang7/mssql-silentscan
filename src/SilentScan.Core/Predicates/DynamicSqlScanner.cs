using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

/// <summary>Finds EXEC(@sql)/EXEC('...')/sp_executesql call sites and buckets them (CLAUDE.md dynamic SQL policy).</summary>
public static class DynamicSqlScanner
{
    public static IReadOnlyList<DynamicSqlFinding> Scan(SqlParseResult parseResult)
    {
        var visitor = new Visitor(parseResult.SourcePath);
        parseResult.Fragment.Accept(visitor);
        return visitor.Findings;
    }

    private sealed class Visitor(string sourcePath) : TSqlFragmentVisitor
    {
        public List<DynamicSqlFinding> Findings { get; } = [];

        public override void Visit(ExecuteStatement node)
        {
            switch (node.ExecuteSpecification.ExecutableEntity)
            {
                case ExecutableStringList stringList:
                    // EXEC(@sql) / EXEC('literal' + ...) - literal-only iff every piece is a
                    // bare string literal (no variables, no expressions to constant-fold).
                    var isLiteralOnly = stringList.Strings.Count > 0 && stringList.Strings.All(s => s is StringLiteral);
                    Findings.Add(new DynamicSqlFinding(sourcePath, node.StartLine, isLiteralOnly));
                    break;

                case ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var name } procRef
                    when string.Equals(name, "sp_executesql", StringComparison.OrdinalIgnoreCase):
                    var firstArg = procRef.Parameters.Count > 0 ? procRef.Parameters[0].ParameterValue : null;
                    Findings.Add(new DynamicSqlFinding(sourcePath, node.StartLine, firstArg is StringLiteral));
                    break;
            }
        }
    }
}

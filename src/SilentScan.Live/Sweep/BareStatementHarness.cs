using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;

namespace SilentScan.Live.Sweep;

public static class BareStatementHarness
{
    public static string WrapBareStatementsInProcedures(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("harness.sql", sql);
        if (parseResult.HasErrors || parseResult.Fragment is not TSqlScript { Batches.Count: > 0 } script)
        {
            return sql;
        }

        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        if (statements.Count == 0)
        {
            return sql;
        }

        var rewritten = new System.Text.StringBuilder();
        var harnessCounter = 0;
        var pendingRun = new List<string>();
        var pendingDdl = new List<string>();

        void FlushRun()
        {
            if (pendingRun.Count == 0)
            {
                return;
            }

            harnessCounter++;
            rewritten.AppendLine(CultureInfo.InvariantCulture, $"CREATE PROCEDURE dbo.__SilentScanHarness_{harnessCounter} AS");
            rewritten.AppendLine("BEGIN");
            foreach (var statementText in pendingRun)
            {
                rewritten.AppendLine(statementText);
            }

            rewritten.AppendLine("END");
            rewritten.AppendLine("GO");
            pendingRun.Clear();
        }

        void FlushDdl()
        {
            if (pendingDdl.Count == 0)
            {
                return;
            }

            foreach (var statementText in pendingDdl)
            {
                rewritten.AppendLine(statementText);
            }

            rewritten.AppendLine("GO");
            pendingDdl.Clear();
        }

        var tokens = parseResult.Fragment.ScriptTokenStream;

        foreach (var statement in statements)
        {
            var leadingStart = LeadingCommentStart(sql, tokens, statement);
            var statementText = sql.Substring(leadingStart, statement.StartOffset + statement.FragmentLength - leadingStart);
            if (IsNonDdlStatement(statement))
            {
                FlushDdl();
                pendingRun.Add(statementText);
            }
            else
            {
                FlushRun();
                if (MustBeFirstStatementInBatch(statement))
                {
                    FlushDdl();
                }

                pendingDdl.Add(statementText);
            }
        }

        FlushRun();
        FlushDdl();
        return rewritten.ToString();
    }

    private static int LeadingCommentStart(string sql, IList<TSqlParserToken>? tokens, TSqlStatement statement)
    {
        if (tokens is null)
        {
            return statement.StartOffset;
        }

        var start = statement.StartOffset;
        var index = statement.FirstTokenIndex - 1;
        var sawNewlineSinceCode = false;

        while (index >= 0)
        {
            var token = tokens[index];
            if (token.TokenType == TSqlTokenType.WhiteSpace)
            {
                if (token.Text?.Contains('\n') == true)
                {
                    sawNewlineSinceCode = true;
                }

                index--;
                continue;
            }

            if (token.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                if (!sawNewlineSinceCode)
                {
                    break;
                }

                start = token.Offset;
                sawNewlineSinceCode = false;
                index--;
                continue;
            }

            break;
        }

        return start;
    }

    private static bool IsNonDdlStatement(TSqlStatement statement)
    {
        if (statement is PredicateSetStatement)
        {
            return false;
        }

        var name = statement.GetType().Name;
        return !name.StartsWith("Create", StringComparison.Ordinal)
            && !name.StartsWith("Alter", StringComparison.Ordinal)
            && !name.StartsWith("Drop", StringComparison.Ordinal);
    }

    private static bool MustBeFirstStatementInBatch(TSqlStatement statement) => statement is
        CreateProcedureStatement or AlterProcedureStatement or CreateOrAlterProcedureStatement
        or CreateViewStatement or AlterViewStatement or CreateOrAlterViewStatement
        or CreateFunctionStatement or AlterFunctionStatement or CreateOrAlterFunctionStatement
        or CreateTriggerStatement or AlterTriggerStatement or CreateOrAlterTriggerStatement;
}

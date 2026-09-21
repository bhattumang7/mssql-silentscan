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

        var writer = new HarnessWriter();
        var tokens = parseResult.Fragment.ScriptTokenStream;

        foreach (var statement in statements)
        {
            var leadingStart = LeadingCommentStart(tokens, statement);
            var statementText = sql.Substring(leadingStart, statement.StartOffset + statement.FragmentLength - leadingStart);
            if (IsNonDdlStatement(statement))
            {
                writer.AddRunStatement(statementText);
            }
            else
            {
                writer.AddDdlStatement(statementText, MustBeFirstStatementInBatch(statement));
            }
        }

        return writer.Finish();
    }

    private sealed class HarnessWriter
    {
        private readonly System.Text.StringBuilder _rewritten = new();
        private readonly List<string> _pendingRun = [];
        private readonly List<string> _pendingDdl = [];
        private int _harnessCounter;

        public void AddRunStatement(string statementText)
        {
            FlushDdl();
            _pendingRun.Add(statementText);
        }

        public void AddDdlStatement(string statementText, bool mustBeFirstInBatch)
        {
            FlushRun();
            if (mustBeFirstInBatch)
            {
                FlushDdl();
            }

            _pendingDdl.Add(statementText);
        }

        public string Finish()
        {
            FlushRun();
            FlushDdl();
            return _rewritten.ToString();
        }

        private void FlushRun()
        {
            if (_pendingRun.Count == 0)
            {
                return;
            }

            _harnessCounter++;
            _rewritten.AppendLine(CultureInfo.InvariantCulture, $"CREATE PROCEDURE dbo.__SilentScanHarness_{_harnessCounter} AS");
            _rewritten.AppendLine("BEGIN");
            foreach (var statementText in _pendingRun)
            {
                _rewritten.AppendLine(statementText);
            }

            _rewritten.AppendLine("END");
            _rewritten.AppendLine("GO");
            _pendingRun.Clear();
        }

        private void FlushDdl()
        {
            if (_pendingDdl.Count == 0)
            {
                return;
            }

            foreach (var statementText in _pendingDdl)
            {
                _rewritten.AppendLine(statementText);
            }

            _rewritten.AppendLine("GO");
            _pendingDdl.Clear();
        }
    }

    private static int LeadingCommentStart(IList<TSqlParserToken>? tokens, TSqlStatement statement)
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

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.TypeInference;

public sealed class BuiltinFixedReturnTypeMatrixTests
{
    private static readonly string[] NotProbed = ["NEWSEQUENTIALID"];

    private static SqlType FromEngineText(string systemTypeName)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"DECLARE @x {systemTypeName};"), out var errors);
        Assert.Empty(errors);
        var declare = (DeclareVariableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null)!;
    }

    public static TheoryData<string> FixedReturnFunctions() =>
        [.. BuiltinFunctionTypeResolver.FixedReturnTypeFunctionNames.Where(n => !NotProbed.Contains(n, StringComparer.OrdinalIgnoreCase)).OrderBy(n => n)];

    [Theory]
    [MemberData(nameof(FixedReturnFunctions))]
    public void FixedReturnType_MatchesWhatTheEngineReportsForEveryProbedShape(string function)
    {
        var ours = BuiltinFunctionTypeResolver.ResolveFixedReturnType(function)!;
        var cells = BuiltinMatrix.Instance.ForFunction(function).Concat(BuiltinMatrix.Latest.ForFunction(function)).Where(c => !c.ResultType.StartsWith(BuiltinMatrix.ErrorPrefix, StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(cells);
        foreach (var cell in cells)
        {
            var engine = FromEngineText(cell.ResultType);
            Assert.Equal(engine.Category, ours.Category);
            if (ours is { LengthKnown: true, Length: not null })
            {
                Assert.Equal(engine.Length, ours.Length);
            }

            if (ours.Scale is not null)
            {
                Assert.Equal(engine.Scale, ours.Scale);
            }
        }
    }
}

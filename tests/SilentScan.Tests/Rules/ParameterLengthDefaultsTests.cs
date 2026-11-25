using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Rules;

public sealed class ParameterLengthDefaultsTests
{
    private static SqlType Resolve(string typeText, int? unsizedDefault)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"DECLARE @x {typeText};"), out var errors);
        Assert.Empty(errors);
        var declare = (DeclareVariableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null, unsizedStringOrBinaryDefaultLength: unsizedDefault)!;
    }

    [Theory]
    [InlineData("VARCHAR")]
    [InlineData("NVARCHAR")]
    [InlineData("CHAR")]
    [InlineData("NCHAR")]
    public void UnsizedCastTarget_ResolvesToTheLengthTheEngineReports(string type)
    {
        var engine = CastMatrix.Instance.TryGetResultType(type)!;

        Assert.Equal($"{type.ToLowerInvariant()}(30)", engine.OfLiteral);
        Assert.Equal(30, Resolve(type, 30).Length);
    }

    [Fact]
    public void UnsizedDeclaredVariable_HasNoExplicitLength_SoTheClassifierAppliesTheEnginesDefaultOfOne()
    {
        var declared = Resolve("VARCHAR", null);

        Assert.Null(declared.Length);
        var column = new SqlType(SqlTypeCategory.VarChar, Length: 10);
        var result = ParameterLengthClassifier.ClassifyUnderLength(column, declared);

        Assert.NotNull(result);
        Assert.True(result.Value.IsImplicitDefault);
    }
}

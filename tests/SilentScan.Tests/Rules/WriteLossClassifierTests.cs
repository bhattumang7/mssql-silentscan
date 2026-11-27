using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Rules;

public sealed class WriteLossClassifierTests
{
    [Theory]
    [InlineData(SqlTypeCategory.Money, SqlTypeCategory.Decimal, WriteLossKind.NumericScaleNarrowing)]
    [InlineData(SqlTypeCategory.Decimal, SqlTypeCategory.Money, WriteLossKind.NumericScaleNarrowing)]
    [InlineData(SqlTypeCategory.SmallMoney, SqlTypeCategory.Money, null)]
    [InlineData(SqlTypeCategory.Int, SqlTypeCategory.Money, WriteLossKind.NumericScaleNarrowing)]
    public void NumericFamily_MoneyPairs_ClassifiedGenerically(SqlTypeCategory targetCategory, SqlTypeCategory sourceCategory, WriteLossKind? expected)
    {
        var target = new SqlType(targetCategory, Precision: targetCategory == SqlTypeCategory.Decimal ? 10 : null, Scale: targetCategory == SqlTypeCategory.Decimal ? 2 : null);
        var source = new SqlType(sourceCategory, Precision: sourceCategory == SqlTypeCategory.Decimal ? 10 : null, Scale: sourceCategory == SqlTypeCategory.Decimal ? 6 : null);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(SqlTypeCategory.Decimal, WriteLossKind.ApproximateToExactTruncation)]
    [InlineData(SqlTypeCategory.Money, WriteLossKind.ApproximateToExactTruncation)]
    [InlineData(SqlTypeCategory.Int, WriteLossKind.ApproximateToExactTruncation)]
    public void FloatSource_NarrowsIntoExactNumericTarget_NotJustIntegers(SqlTypeCategory targetCategory, WriteLossKind expected)
    {
        var target = new SqlType(targetCategory, Precision: targetCategory == SqlTypeCategory.Decimal ? 10 : null, Scale: targetCategory == SqlTypeCategory.Decimal ? 2 : null);
        var source = new SqlType(SqlTypeCategory.Float);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(expected, kind);
    }

    [Fact]
    public void FloatSource_IntoNarrowerRealTarget_Flags()
    {
        var target = new SqlType(SqlTypeCategory.Real);
        var source = new SqlType(SqlTypeCategory.Float);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.NumericScaleNarrowing, kind);
    }

    [Fact]
    public void RealSource_IntoFloatTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.Float);
        var source = new SqlType(SqlTypeCategory.Real);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void FloatSource_IntoFloatTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.Float);
        var source = new SqlType(SqlTypeCategory.Float);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void Length_NarrowerVarcharTarget_VariableTarget_Flags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 3);
        var source = new SqlType(SqlTypeCategory.VarChar, Length: 10);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.LengthTruncation, kind);
    }

    [Fact]
    public void Length_NarrowerVarcharTarget_TableColumnTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 3);
        var source = new SqlType(SqlTypeCategory.VarChar, Length: 10);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: false);

        Assert.Null(kind);
    }

    [Fact]
    public void Length_NarrowerVarbinaryTarget_VariableTarget_Flags()
    {
        var target = new SqlType(SqlTypeCategory.VarBinary, Length: 4);
        var source = new SqlType(SqlTypeCategory.VarBinary, Length: 16);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.LengthTruncation, kind);
    }

    [Fact]
    public void Length_MaxTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, IsMax: true);
        var source = new SqlType(SqlTypeCategory.VarChar, Length: 10);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void Length_UnknownSourceLength_NeverGuesses()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 3);
        var source = new SqlType(SqlTypeCategory.VarChar, Length: null);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void TemporalScaleNarrowing_DateTime2ToNarrowerDateTime2_Flags()
    {
        var target = new SqlType(SqlTypeCategory.DateTime2, Scale: 2);
        var source = new SqlType(SqlTypeCategory.DateTime2, Scale: 7);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.TemporalScaleNarrowing, kind);
    }

    [Fact]
    public void TemporalScaleNarrowing_DateTime2ToTime_Flags()
    {
        var target = new SqlType(SqlTypeCategory.Time, Scale: 0);
        var source = new SqlType(SqlTypeCategory.DateTime2, Scale: 7);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.TemporalScaleNarrowing, kind);
    }

    [Fact]
    public void TemporalOffsetDropped_DateTimeOffsetIntoDateTime2_Flags()
    {
        var target = new SqlType(SqlTypeCategory.DateTime2, Scale: 3);
        var source = new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 3);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.TemporalOffsetDropped, kind);
    }

    [Fact]
    public void TemporalOffsetDropped_TakesPriorityOverScaleNarrowing_ForSameArgument()
    {
        var target = new SqlType(SqlTypeCategory.DateTime2, Scale: 2);
        var source = new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(WriteLossKind.TemporalOffsetDropped, kind);
    }

    [Fact]
    public void TemporalOffsetDropped_IntoDateTimeOffsetTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 3);
        var source = new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 3);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void UnicodeReplacement_Utf8CollationTarget_NeverFlags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 20, Collation: new Collation("Latin1_General_100_CI_AS_SC_UTF8"));
        var source = new SqlType(SqlTypeCategory.NVarChar, Length: 5);
        var literal = new StringLiteral { IsNational = true, Value = "龍龍龍龍龍" };

        var kind = WriteLossClassifier.Classify(target, source, literal, isVariableTarget: true);

        Assert.Null(kind);
    }

    [Fact]
    public void UnicodeReplacement_NonUtf8CollationTarget_StillFlags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 20);
        var source = new SqlType(SqlTypeCategory.NVarChar, Length: 5);
        var literal = new StringLiteral { IsNational = true, Value = "龍龍龍龍龍" };

        var kind = WriteLossClassifier.Classify(target, source, literal, isVariableTarget: true);

        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, kind);
    }

    [Fact]
    public void Bit_IsNotPulledIntoNumericScaleNarrowing()
    {
        var target = new SqlType(SqlTypeCategory.Bit);
        var source = new SqlType(SqlTypeCategory.Int);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Null(kind);
    }

    private static ScalarExpression ParseExpression(string sql)
    {
        var result = SilentScan.Core.Parsing.SqlScriptParser.ParseText("test.sql", $"SELECT {sql}");
        Assert.False(result.HasErrors, string.Join(';', result.Errors.Select(e => e.Message)));
        var script = Assert.IsType<TSqlScript>(result.Fragment);
        var select = Assert.IsType<SelectStatement>(script.Batches[0].Statements[0]);
        var querySpec = Assert.IsType<QuerySpecification>(select.QueryExpression);
        return Assert.IsType<SelectScalarExpression>(querySpec.SelectElements[0]).Expression;
    }

    [Theory]
    [InlineData("CAST(@v AS NVARCHAR(10))", SqlTypeCategory.Int, null)]
    [InlineData("N'id:' + CONVERT(NVARCHAR(20), @v)", SqlTypeCategory.Decimal, null)]
    [InlineData("CONVERT(NVARCHAR(40), @v)", SqlTypeCategory.UniqueIdentifier, null)]
    [InlineData("CAST(@v AS NVARCHAR(5))", SqlTypeCategory.Bit, null)]
    [InlineData("CAST(@v AS NVARCHAR(30))", SqlTypeCategory.Float, null)]
    [InlineData("CAST(@v AS NVARCHAR(10))", SqlTypeCategory.NVarChar, WriteLossKind.UnicodeToNonUnicodeReplacement)]
    [InlineData("CAST(@v AS NVARCHAR(30))", SqlTypeCategory.DateTime, WriteLossKind.UnicodeToNonUnicodeReplacement)]
    public void UnicodeReplacement_CastOfNonTextSource_IsProvablyAscii(string expression, SqlTypeCategory operandCategory, WriteLossKind? expected)
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 50);
        var source = new SqlType(SqlTypeCategory.NVarChar, Length: 40);

        var kind = WriteLossClassifier.Classify(
            target, source, ParseExpression(expression), isVariableTarget: true,
            operandType: e => e is VariableReference ? new SqlType(operandCategory, Length: 10) : null);

        Assert.Equal(expected, kind);
    }

    [Fact]
    public void UnicodeReplacement_CastWhoseOperandTypeIsUnknown_StillFlags()
    {
        var target = new SqlType(SqlTypeCategory.VarChar, Length: 50);
        var source = new SqlType(SqlTypeCategory.NVarChar, Length: 40);

        var kind = WriteLossClassifier.Classify(target, source, ParseExpression("CAST(@v AS NVARCHAR(10))"), isVariableTarget: true);

        Assert.Equal(WriteLossKind.UnicodeToNonUnicodeReplacement, kind);
    }

    [Theory]
    [InlineData(SqlTypeCategory.VarChar, null, true, SqlTypeCategory.VarChar, 5, null, true)]
    [InlineData(SqlTypeCategory.VarBinary, null, true, SqlTypeCategory.VarBinary, 5, null, true)]
    [InlineData(SqlTypeCategory.VarBinary, 10, false, SqlTypeCategory.VarChar, 5, null, true)]
    [InlineData(SqlTypeCategory.VarBinary, 10, false, SqlTypeCategory.NVarChar, 5, null, false)]
    [InlineData(SqlTypeCategory.VarBinary, 11, false, SqlTypeCategory.NVarChar, 5, null, true)]
    [InlineData(SqlTypeCategory.DateTime2, null, false, SqlTypeCategory.VarChar, 10, 7, true)]
    [InlineData(SqlTypeCategory.DateTime2, null, false, SqlTypeCategory.VarChar, 27, 7, false)]
    [InlineData(SqlTypeCategory.Date, null, false, SqlTypeCategory.VarChar, 10, null, false)]
    [InlineData(SqlTypeCategory.Date, null, false, SqlTypeCategory.VarChar, 7, null, true)]
    [InlineData(SqlTypeCategory.Time, null, false, SqlTypeCategory.VarChar, 5, 0, true)]
    [InlineData(SqlTypeCategory.DateTime, null, false, SqlTypeCategory.VarChar, 10, null, true)]
    [InlineData(SqlTypeCategory.DateTimeOffset, null, false, SqlTypeCategory.VarChar, 27, 7, true)]
    public void LengthTruncation_CoversMaxAndCrossFamilySourcesIntoVariables(
        SqlTypeCategory sourceCategory, int? sourceLength, bool sourceIsMax, SqlTypeCategory targetCategory, int targetLength, int? sourceScale, bool fires)
    {
        var source = new SqlType(sourceCategory, Length: sourceLength, IsMax: sourceIsMax, Scale: sourceScale);
        var target = new SqlType(targetCategory, Length: targetLength);

        var kind = WriteLossClassifier.Classify(target, source, sourceExpression: null, isVariableTarget: true);

        Assert.Equal(fires, kind == WriteLossKind.LengthTruncation);
    }
}

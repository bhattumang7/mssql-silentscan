using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class SetOptionMatrixTests
{
    [Theory]
    [InlineData("concat-null", "NULL-RESULT", "[]")]
    [InlineData("equals-null", "nomatch", "match")]
    [InlineData("padding-trailing-space", "5", "2")]
    [InlineData("warnings-truncation", "Error:2628", "abc")]
    [InlineData("datefirst-weekday", "1", "7")]
    [InlineData("rowcount-limit-rows", "3", "1")]
    [InlineData("implicit-transactions-trancount", "1", "0")]
    public void Option_ChangesTheOutcomeAsTheEngineReports(string id, string whenOn, string whenOff)
    {
        var cell = SetOptionMatrix.Instance.TryGet(id);

        Assert.NotNull(cell);
        Assert.Equal(whenOn, cell.WhenOn);
        Assert.Equal(whenOff, cell.WhenOff);
        Assert.True(cell.Differs);
    }

    [Theory]
    [InlineData("dateformat-iso-date")]
    [InlineData("warnings-overflow-null")]
    [InlineData("textsize-truncation")]
    public void Option_DoesNotChangeTheOutcome_WhereTheEngineIgnoresIt(string id)
    {
        Assert.False(SetOptionMatrix.Instance.TryGet(id)!.Differs);
    }

    [Fact]
    public void IsoDateLiteralIsImmuneToDateformat_ButIsoDatetimeLiteralIsNot()
    {
        Assert.False(SetOptionMatrix.Instance.TryGet("dateformat-iso-date")!.Differs);
        Assert.True(SetOptionMatrix.Instance.TryGet("dateformat-iso-datetime")!.Differs);
    }

    [Fact]
    public void Matrix_HasUniqueIds()
    {
        var ids = SetOptionMatrix.Instance.Entries.Select(e => e.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(SetOptionMatrix.Instance.ForOption("ANSI_NULLS").Count() >= 2);
    }
}

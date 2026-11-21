using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;
using Xunit.Sdk;

namespace SilentScan.Tests.Architecture;

[Trait("Category", "Oracle")]
public sealed class RuleOracleBaseTests
{
    private const string Setup = """
        CREATE TABLE dbo.Parent (Id INT NOT NULL);
        GO
        CREATE TABLE dbo.ChildNullable (RefId INT NULL);
        GO
        CREATE TABLE dbo.ChildNotNull (RefId INT NOT NULL);
        GO
        INSERT INTO dbo.Parent (Id) VALUES (1), (2), (3);
        INSERT INTO dbo.ChildNullable (RefId) VALUES (1), (NULL);
        INSERT INTO dbo.ChildNotNull (RefId) VALUES (1);
        GO
        """;

    private const string FiringStatement = "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);";

    private const string QuietStatement = "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);";

    private static readonly OracleCase Firing = new("firing", Setup, FiringStatement);

    private static readonly SilentCase Quiet = new(
        new OracleCase("quiet", Setup, QuietStatement),
        new NotInNullableSubqueryFinding("Id", "dbo.ChildNotNull", "RefId", false, "claimed.sql", 1, 1),
        "firing");

    [Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
    private sealed class Probe(IReadOnlyList<OracleCase> firing, IReadOnlyList<SilentCase> silent, IRuleVerifier? verifier = null) : RuleOracle
    {
        protected override IReadOnlyList<OracleCase> FiringCases => firing;

        protected override IReadOnlyList<SilentCase> SilentCases => silent;

        protected override IRuleVerifier Verifier => verifier ?? base.Verifier;
    }

    private sealed class LookupVerifier : IRuleVerifier
    {
        public string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

        public Type FindingType => typeof(NotInNullableSubqueryFinding);

        public bool Judges(IFinding finding) => finding is NotInNullableSubqueryFinding;

        public async Task<EngineVerification> VerifyAsync(
            SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken = default)
        {
            var claim = (NotInNullableSubqueryFinding)finding;
            await using var connection = new SqlConnection(options.BuildConnectionString(database));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c;", connection);
            command.Parameters.AddWithValue("@t", claim.SubqueryTableQualifiedName);
            command.Parameters.AddWithValue("@c", claim.SubqueryColumnName);
            var nullable = await command.ExecuteScalarAsync(cancellationToken) is true;
            return new EngineVerification(nullable ? EngineVerdict.Confirmed : EngineVerdict.Refuted, "answered from sys.columns");
        }
    }

    private sealed class FieldsOnlyProbeVerifier : IRuleVerifier
    {
        public string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

        public Type FindingType => typeof(NotInNullableSubqueryFinding);

        public bool Judges(IFinding finding) => finding is NotInNullableSubqueryFinding;

        public async Task<EngineVerification> VerifyAsync(
            SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken = default)
        {
            var claim = (NotInNullableSubqueryFinding)finding;
            await using var connection = new SqlConnection(options.BuildConnectionString(database));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                $"SELECT COUNT(*) FROM {claim.SubqueryTableQualifiedName} WHERE {claim.SubqueryColumnName} IS NULL;", connection);
            var nulls = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            return new EngineVerification(nulls > 0 ? EngineVerdict.Confirmed : EngineVerdict.Refuted, "answered from a probe built from the finding's fields");
        }
    }

    private sealed class ConfirmsEverythingVerifier : IRuleVerifier
    {
        public string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

        public Type FindingType => typeof(NotInNullableSubqueryFinding);

        public bool Judges(IFinding finding) => finding is NotInNullableSubqueryFinding;

        public Task<EngineVerification> VerifyAsync(
            SqlServerOptions options, string database, IFinding finding, string statement, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EngineVerification(EngineVerdict.Confirmed, "never looked at the statement"));
    }

    private static Task<XunitException> FiringFailureAsync(params OracleCase[] cases) =>
        Assert.ThrowsAnyAsync<XunitException>(() => new Probe(cases, [Quiet]).FiringCases_ScannerFlagsEachAndTheEngineConfirmsEveryFinding());

    private static Task<XunitException> SilentFailureAsync(params SilentCase[] cases) =>
        Assert.ThrowsAnyAsync<XunitException>(() => new Probe([Firing], cases).SilentCases_ScannerStaysSilentAndTheEngineRefutesEachClaim());

    private static Task<XunitException> BadVerifierFailureAsync(IRuleVerifier verifier) =>
        Assert.ThrowsAnyAsync<XunitException>(() => new Probe([Firing], [Quiet], verifier).FiringCases_ScannerFlagsEachAndTheEngineConfirmsEveryFinding());

    [Fact]
    public async Task WellFormedCases_Pass()
    {
        var oracle = new Probe([Firing], [Quiet]);

        await oracle.FiringCases_ScannerFlagsEachAndTheEngineConfirmsEveryFinding();
        await oracle.SilentCases_ScannerStaysSilentAndTheEngineRefutesEachClaim();
    }

    [Fact]
    public async Task FiringList_NamesTheCaseThatTheScannerDoesNotFlag()
    {
        var failure = await FiringFailureAsync(Firing, new OracleCase("quiet-listed-as-firing", Setup, QuietStatement));

        Assert.Contains("quiet-listed-as-firing", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[firing]", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SilentList_NamesTheCaseOnWhichTheScannerFires()
    {
        var failure = await SilentFailureAsync(Quiet, new SilentCase(new OracleCase("firing-listed-as-silent", Setup, FiringStatement), Quiet.Claim, "firing"));

        Assert.Contains("firing-listed-as-silent", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[quiet]", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SilentList_NamesTheCaseWhoseClaimTheEngineDoesNotRefute()
    {
        var unrefutable = new SilentCase(
            new OracleCase("claim-the-engine-confirms", Setup, QuietStatement),
            new NotInNullableSubqueryFinding("Id", "dbo.ChildNullable", "RefId", false, "claimed.sql", 1, 1),
            "firing");

        var failure = await SilentFailureAsync(Quiet, unrefutable);

        Assert.Contains("claim-the-engine-confirms", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyFiringList_Fails()
    {
        var failure = await FiringFailureAsync();

        Assert.Contains("at least one", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptySilentList_Fails()
    {
        var failure = await SilentFailureAsync();

        Assert.Contains("at least one", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateCaseNames_Fail()
    {
        var failure = await FiringFailureAsync(Firing, Firing);

        Assert.Contains("duplicate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BlankStatement_Fails()
    {
        var failure = await FiringFailureAsync(new OracleCase("blank", Setup, "   "));

        Assert.Contains("blank", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatementHoldingTwoStatements_Fails()
    {
        var failure = await FiringFailureAsync(new OracleCase("two-statements", Setup, FiringStatement + "\n" + FiringStatement));

        Assert.Contains("exactly one", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SilentCaseNamingAFiringPartnerThatDoesNotExist_Fails()
    {
        var failure = await SilentFailureAsync(Quiet with { FiringPartner = "no-such-case" });

        Assert.Contains("no-such-case", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SilentCaseWhoseSetupDiffersFromItsFiringPartner_Fails()
    {
        var different = Quiet with { Case = Quiet.Case with { Setup = Setup + "\n-- different" } };

        var failure = await SilentFailureAsync(different);

        Assert.Contains("share its setup", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifierThatAnswersFromACatalogLookup_IsRejectedByTheStatementSwapControl()
    {
        var failure = await BadVerifierFailureAsync(new LookupVerifier());

        Assert.Contains("not judging the statement", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifierThatProbesFromTheFindingsFieldsOnly_IsRejectedByTheStatementSwapControl()
    {
        var failure = await BadVerifierFailureAsync(new FieldsOnlyProbeVerifier());

        Assert.Contains("not judging the statement", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifierThatIgnoresTheStatementEntirely_IsRejectedByTheStatementSwapControl()
    {
        var failure = await BadVerifierFailureAsync(new ConfirmsEverythingVerifier());

        Assert.Contains("not judging the statement", failure.Message, StringComparison.Ordinal);
    }
}

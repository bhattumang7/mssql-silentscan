using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

public abstract class DatabaseFlagOracle : RuleOracle
{
    protected abstract string DatabaseOptionName { get; }

    protected abstract string FiringValue { get; }

    protected abstract string QuietValue { get; }

    protected abstract DatabaseConfigurationFindingKind Kind { get; }

    protected override OracleCase Fires => Case(FiringValue);

    protected override OracleCase Silent => Case(QuietValue);

    protected override IFinding ClaimedOnSilent => new DatabaseConfigurationFinding(Kind, "claimed");

    private OracleCase Case(string value) => new(
        $"""
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);
        GO
        ALTER DATABASE CURRENT SET {DatabaseOptionName} {value};
        GO
        """,
        string.Empty);
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoShrinkOnRuleId)]
public sealed class AutoShrinkOnOracleTests : DatabaseFlagOracle
{
    protected override string DatabaseOptionName => "AUTO_SHRINK";

    protected override string FiringValue => "ON";

    protected override string QuietValue => "OFF";

    protected override DatabaseConfigurationFindingKind Kind => DatabaseConfigurationFindingKind.AutoShrinkOn;
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoCloseOnRuleId)]
public sealed class AutoCloseOnOracleTests : DatabaseFlagOracle
{
    protected override string DatabaseOptionName => "AUTO_CLOSE";

    protected override string FiringValue => "ON";

    protected override string QuietValue => "OFF";

    protected override DatabaseConfigurationFindingKind Kind => DatabaseConfigurationFindingKind.AutoCloseOn;
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoCreateStatisticsOffRuleId)]
public sealed class AutoCreateStatisticsOffOracleTests : DatabaseFlagOracle
{
    protected override string DatabaseOptionName => "AUTO_CREATE_STATISTICS";

    protected override string FiringValue => "OFF";

    protected override string QuietValue => "ON";

    protected override DatabaseConfigurationFindingKind Kind => DatabaseConfigurationFindingKind.AutoCreateStatisticsOff;
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoUpdateStatisticsOffRuleId)]
public sealed class AutoUpdateStatisticsOffOracleTests : DatabaseFlagOracle
{
    protected override string DatabaseOptionName => "AUTO_UPDATE_STATISTICS";

    protected override string FiringValue => "OFF";

    protected override string QuietValue => "ON";

    protected override DatabaseConfigurationFindingKind Kind => DatabaseConfigurationFindingKind.AutoUpdateStatisticsOff;
}

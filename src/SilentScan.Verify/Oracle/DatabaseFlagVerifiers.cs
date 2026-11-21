using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public abstract class DatabaseFlagVerifier : FactVerifier<DatabaseConfigurationFinding>
{
    protected abstract string DatabasePropertyName { get; }

    protected abstract int ClaimedValue { get; }

    protected override string FactSql(DatabaseConfigurationFinding finding) =>
        $"SELECT CASE WHEN CAST(DATABASEPROPERTYEX(DB_NAME(), {Literal(DatabasePropertyName)}) AS INT) = {ClaimedValue} THEN 1 ELSE 0 END;";

    protected override string FactDescription(DatabaseConfigurationFinding finding) =>
        $"{DatabasePropertyName} is {ClaimedValue} for the current database";
}

public sealed class AutoShrinkOnVerifier : DatabaseFlagVerifier
{
    public override string RuleId => FindingRuleIds.DatabaseConfigurationAutoShrinkOnRuleId;

    protected override string DatabasePropertyName => "IsAutoShrink";

    protected override int ClaimedValue => 1;
}

public sealed class AutoCloseOnVerifier : DatabaseFlagVerifier
{
    public override string RuleId => FindingRuleIds.DatabaseConfigurationAutoCloseOnRuleId;

    protected override string DatabasePropertyName => "IsAutoClose";

    protected override int ClaimedValue => 1;
}

public sealed class AutoCreateStatisticsOffVerifier : DatabaseFlagVerifier
{
    public override string RuleId => FindingRuleIds.DatabaseConfigurationAutoCreateStatisticsOffRuleId;

    protected override string DatabasePropertyName => "IsAutoCreateStatistics";

    protected override int ClaimedValue => 0;
}

public sealed class AutoUpdateStatisticsOffVerifier : DatabaseFlagVerifier
{
    public override string RuleId => FindingRuleIds.DatabaseConfigurationAutoUpdateStatisticsOffRuleId;

    protected override string DatabasePropertyName => "IsAutoUpdateStatistics";

    protected override int ClaimedValue => 0;
}

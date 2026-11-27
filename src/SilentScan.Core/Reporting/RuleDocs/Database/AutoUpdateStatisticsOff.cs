using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Database;

internal static class AutoUpdateStatisticsOff
{
    public static string RuleId => SarifRuleCatalog.DatabaseConfigurationRuleId(DatabaseConfigurationFindingKind.AutoUpdateStatisticsOff);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            With `AUTO_UPDATE_STATISTICS` off, statistics are no longer refreshed automatically as data changes - they stay stale
            until a manual UPDATE STATISTICS, sp_updatestats or an index rebuild, so plans drift
            from the table's real data distribution in between. `AUTO_UPDATE_STATISTICS` being ON is the
            engine's own out-of-the-box default, and this rule reports the same severity class as
            its `auto-create-statistics-off` sibling: a long-established, essentially
            uncontroversial anti-pattern to have turned off, not a deliberate operational choice the
            way Query Store's own settings are.

            This is a database-level configuration fact, read once per scan directly from
            `sys.databases.is_auto_update_stats_on` - only available when scanning a live, connected
            target, since there is no file-mode equivalent of "the database's own current
            configuration."
            """,
        HowToFixIt: """
            Turn AUTO_UPDATE_STATISTICS ON so statistics keep refreshing as the underlying data
            changes.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A database with AUTO_UPDATE_STATISTICS turned off",
                NoncompliantSql: """
                    ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS OFF;
                    """,
                NoncompliantExplanation: "Statistics are no longer refreshed automatically - plans compiled against them drift from reality until a manual UPDATE STATISTICS, sp_updatestats or an index rebuild.",
                CompliantSql: """
                    ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS ON;
                    """,
                CompliantExplanation: "Restores the engine's own default behavior - statistics automatically refresh as the underlying data changes."),
        ]);
}

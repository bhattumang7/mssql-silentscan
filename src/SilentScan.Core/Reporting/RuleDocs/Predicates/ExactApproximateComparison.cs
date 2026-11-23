using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Predicates;

internal static class ExactApproximateComparison
{
    public static string RuleId => SarifRuleCatalog.ExactApproximateComparisonRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            When a comparison has an exact numeric type on one side and `FLOAT` or `REAL` on the other,
            SQL Server converts the exact side to the approximate type before comparing, because the
            approximate types rank higher in data type precedence. A `FLOAT` carries about 15
            significant decimal digits and a `REAL` about 7, so an exact value with more digits than
            that is rounded during the conversion. Two different exact values can round to the same
            approximate value and then compare as equal.

            The effect is silent and applies to every comparison operator, not only equality: a
            `BIGINT` of 9007199254740993 compares equal to a `FLOAT` of 9007199254740992, and a range
            predicate such as `>` treats the two as the same number and leaves the row out. A
            `BIGINT`, a `DECIMAL` wider than 15 digits or `MONEY` against `FLOAT`, and an `INT`
            or `DECIMAL` wider than 7 digits against `REAL`, are all affected. An `INT` against a
            `FLOAT` is not, since a `FLOAT` holds every 32-bit integer exactly.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A BIGINT identifier compared with a FLOAT column matches a different row",
                NoncompliantSql: """
                    CREATE TABLE dbo.Accounts (AccountId BIGINT NOT NULL PRIMARY KEY);
                    CREATE TABLE dbo.Imports (AccountRef FLOAT NOT NULL);

                    SELECT a.AccountId
                    FROM dbo.Accounts AS a
                    JOIN dbo.Imports AS i ON i.AccountRef = a.AccountId;
                    """,
                NoncompliantExplanation: "AccountId is converted to FLOAT before the comparison, so accounts 9007199254740992 and 9007199254740993 both match an import row holding 9007199254740992, and the join returns the wrong account with no error.",
                CompliantSql: """
                    CREATE TABLE dbo.Accounts (AccountId BIGINT NOT NULL PRIMARY KEY);
                    CREATE TABLE dbo.Imports (AccountRef BIGINT NOT NULL);

                    SELECT a.AccountId
                    FROM dbo.Accounts AS a
                    JOIN dbo.Imports AS i ON i.AccountRef = a.AccountId;
                    """,
                CompliantExplanation: "Both columns share the exact type BIGINT, so the comparison is exact and each identifier matches only itself. Where the approximate column must stay, compare against CAST(i.AccountRef AS DECIMAL(19, 0)) so the conversion goes to an exact type."),
        ]);
}

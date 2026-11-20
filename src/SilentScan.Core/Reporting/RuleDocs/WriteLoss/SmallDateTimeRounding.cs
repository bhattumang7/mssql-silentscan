using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.WriteLoss;

internal static class SmallDateTimeRounding
{
    public static string RuleId => SarifRuleCatalog.WriteLossSmallDateTimeRoundingRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            SMALLDATETIME stores whole minutes only, with an accuracy of one minute. When a
            DATETIME or DATETIME2 value, or a string with a seconds component, is assigned into a
            SMALLDATETIME target, SQL Server does not raise an error. It rounds the value to the
            nearest minute: 30 seconds or more rounds up, less than 30 seconds rounds down. A value
            of 10:20:45.500 is stored as 10:21:00, and 10:20:29 is stored as 10:20:00.

            The change is small enough to go unnoticed, but it is real. Two events a few seconds
            apart can collapse into the same stored value, an event can end up recorded a minute
            later than it happened, and a later comparison against the original value no longer
            matches. The same rounding applies to variable assignment and to INSERT or UPDATE into a
            table column.
            """,
        HowToFixIt: """
            Declare the target as DATETIME2 with the precision the data needs if the seconds
            matter. If whole minutes are genuinely intended, round explicitly, for example with
            DATEADD(MINUTE, DATEDIFF(MINUTE, 0, value), 0), so the loss is a visible decision in
            the query text instead of a side effect of the target's declared type.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A DATETIME2 value written into a SMALLDATETIME column",
                NoncompliantSql: """
                    CREATE TABLE dbo.Slot (Id INT NOT NULL, StartsAt SMALLDATETIME NOT NULL);

                    CREATE PROCEDURE dbo.AddSlot (@id INT)
                    AS
                    BEGIN
                        INSERT INTO dbo.Slot (Id, StartsAt) VALUES (@id, SYSDATETIME());
                    END;
                    """,
                NoncompliantExplanation: "SYSDATETIME() returns a DATETIME2 value with seconds; StartsAt is SMALLDATETIME, so the stored value is silently rounded to the nearest minute.",
                CompliantSql: """
                    CREATE TABLE dbo.Slot (Id INT NOT NULL, StartsAt DATETIME2(0) NOT NULL);

                    CREATE PROCEDURE dbo.AddSlot (@id INT)
                    AS
                    BEGIN
                        INSERT INTO dbo.Slot (Id, StartsAt) VALUES (@id, SYSDATETIME());
                    END;
                    """,
                CompliantExplanation: "StartsAt is now DATETIME2(0), which keeps the seconds, so nothing is rounded away."),
        ]);
}

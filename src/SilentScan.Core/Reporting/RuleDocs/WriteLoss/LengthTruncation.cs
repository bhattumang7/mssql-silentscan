using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.WriteLoss;

internal static class LengthTruncation
{
    public static string RuleId => SarifRuleCatalog.WriteLossLengthTruncationRuleId;

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            VARCHAR/NVARCHAR/CHAR/NCHAR and BINARY/VARBINARY are all bounded by a declared length.
            When a value from a longer variable is assigned into a shorter one with a plain SET or
            SELECT, SQL Server does not raise an error. It silently keeps only the leading
            characters/bytes that fit and discards the rest.

            This is scoped to variable targets specifically, because the same narrowing behaves
            completely differently against a table column: inserting or updating a table column
            with a value longer than its declared length raises a hard error ("String or binary
            data would be truncated"), not a silent loss. So this rule only fires where the loss is
            actually silent - local variable assignment - never for INSERT/UPDATE into a real
            column, where the engine already stops you.

            The same narrowing happening across a stored procedure call boundary instead - a
            parameter passed in, or an OUTPUT parameter's final value copied back to the caller -
            is tracked separately under the call-graph argument-type-mismatch rule, since that
            mismatch is only decidable by resolving the call site against the callee's own
            `sys.parameters` declaration rather than from a single routine's own text.
            """,
        HowToFixIt: """
            Declare the receiving variable with a length at least as long as the source it's
            assigned from, so nothing is silently cut off. If a shorter value is genuinely
            intended, make that explicit with LEFT() or SUBSTRING() at the assignment site, so a
            reader sees the truncation as a deliberate decision rather than an invisible consequence
            of the variable's declared length.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "A wider variable's value copied into a narrower one",
                NoncompliantSql: """
                    CREATE PROCEDURE dbo.BuildReference AS
                    BEGIN
                        DECLARE @Reference VARCHAR(10) = 'REF-000123';
                        DECLARE @CallerReference VARCHAR(3);
                        SET @CallerReference = @Reference;
                    END;
                    """,
                NoncompliantExplanation: "@Reference holds a 10-character value; assigning it into @CallerReference VARCHAR(3) silently keeps only 'REF' and drops the rest, with no error.",
                CompliantSql: """
                    CREATE PROCEDURE dbo.BuildReference AS
                    BEGIN
                        DECLARE @Reference VARCHAR(10) = 'REF-000123';
                        DECLARE @CallerReference VARCHAR(10);
                        SET @CallerReference = @Reference;
                    END;
                    """,
                CompliantExplanation: "@CallerReference is now declared with the same length as @Reference, so the full value survives the assignment."),
        ]);
}

using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Predicates;

internal static class RowLimitOutOfRange
{
    internal static class TopRowCountNegative
    {
        public static string RuleId => SarifRuleCatalog.RowLimitOutOfRangeRuleId(RowLimitOutOfRangeKind.TopRowCountNegative);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                A `TOP (n)` row-count clause's argument, when it folds to a literal constant,
                must not be negative. Confirmed directly against a real SQL Server instance:
                `TOP (-1)` fails to compile with Msg 127 ("A TOP N or FETCH rowcount value may
                not be negative"), regardless of which statement form (`SELECT`, or the `TOP`
                clause on `UPDATE`/`DELETE`/`INSERT`) uses it. `TOP (0)` is not an error - it is
                a valid, if unusual, way to select zero rows.

                Only literal constants are checked - a parameter or variable's value is not
                statically known, and evaluating a negative `TOP` argument from a variable fails
                at execution time instead, not at compile time.
                """,
            HowToFixIt: """
                Use a non-negative literal row count, or `TOP (0)` if intentionally selecting no
                rows.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A negative literal TOP row count never compiles",
                    NoncompliantSql: """
                        SELECT TOP (-1) * FROM dbo.Customer;
                        """,
                    NoncompliantExplanation: "A negative TOP row count fails to compile with Msg 127 every time this statement runs.",
                    CompliantSql: """
                        SELECT TOP (0) * FROM dbo.Customer;
                        """,
                    CompliantExplanation: "Zero is a valid TOP row count - it compiles and simply returns no rows."),
            ]);
    }

    internal static class OffsetNegative
    {
        public static string RuleId => SarifRuleCatalog.RowLimitOutOfRangeRuleId(RowLimitOutOfRangeKind.OffsetNegative);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                An `OFFSET ... ROWS` clause's literal row count must not be negative. Confirmed
                directly against a real SQL Server instance: `OFFSET -1 ROWS` fails to compile
                with Msg 10742 ("The offset specified in a OFFSET clause may not be negative").
                `OFFSET 0 ROWS` is not an error - it skips no rows. Only literal constants are
                checked - a parameter or variable's value is not statically known.
                """,
            HowToFixIt: """
                Use a non-negative literal offset, or `OFFSET 0 ROWS` to skip no rows.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A negative literal OFFSET never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer ORDER BY CustomerId OFFSET -1 ROWS;
                        """,
                    NoncompliantExplanation: "A negative OFFSET fails to compile with Msg 10742 every time this statement runs.",
                    CompliantSql: """
                        SELECT * FROM dbo.Customer ORDER BY CustomerId OFFSET 0 ROWS;
                        """,
                    CompliantExplanation: "Zero is a valid OFFSET - it compiles and skips no rows."),
            ]);
    }

    internal static class FetchNotPositive
    {
        public static string RuleId => SarifRuleCatalog.RowLimitOutOfRangeRuleId(RowLimitOutOfRangeKind.FetchNotPositive);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                A `FETCH NEXT ... ROWS ONLY` clause's literal row count must be strictly greater
                than zero - unlike `OFFSET` and `TOP`, a `FETCH` count of exactly 0 is also
                rejected. Confirmed directly against a real SQL Server instance: both
                `FETCH NEXT -1 ROWS ONLY` and `FETCH NEXT 0 ROWS ONLY` fail to compile with Msg
                10744 ("The number of rows provided for a FETCH clause must be greater then
                zero"). Only literal constants are checked - a parameter or variable's value is
                not statically known.
                """,
            HowToFixIt: """
                Use a strictly positive literal row count.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A zero or negative literal FETCH count never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer ORDER BY CustomerId OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;
                        """,
                    NoncompliantExplanation: "A FETCH count of zero fails to compile with Msg 10744 every time this statement runs - unlike OFFSET and TOP, FETCH does not accept zero.",
                    CompliantSql: """
                        SELECT * FROM dbo.Customer ORDER BY CustomerId OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
                        """,
                    CompliantExplanation: "A strictly positive FETCH count compiles."),
            ]);
    }

    internal static class TableSamplePercentOutOfRange
    {
        public static string RuleId => SarifRuleCatalog.RowLimitOutOfRangeRuleId(RowLimitOutOfRangeKind.TableSamplePercentOutOfRange);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                A `TABLESAMPLE (n PERCENT)` clause's literal argument must fall within 0 to 100
                inclusive. Confirmed directly against a real SQL Server instance:
                `TABLESAMPLE (150 PERCENT)` and `TABLESAMPLE (-5 PERCENT)` both fail to compile
                with Msg 476 ("The PERCENT tablesample size must be between 0 and 100"). Only
                literal constants are checked - a parameter or variable's value is not statically
                known.
                """,
            HowToFixIt: """
                Use a literal percent value between 0 and 100 inclusive.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A TABLESAMPLE PERCENT literal above 100 never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer TABLESAMPLE (150 PERCENT);
                        """,
                    NoncompliantExplanation: "A PERCENT value over 100 fails to compile with Msg 476 every time this statement runs.",
                    CompliantSql: """
                        SELECT * FROM dbo.Customer TABLESAMPLE (10 PERCENT);
                        """,
                    CompliantExplanation: "A percent value within 0-100 compiles."),
            ]);
    }

    internal static class TableSampleRowsNotPositive
    {
        public static string RuleId => SarifRuleCatalog.RowLimitOutOfRangeRuleId(RowLimitOutOfRangeKind.TableSampleRowsNotPositive);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                A `TABLESAMPLE (n ROWS)` clause's literal row count must be strictly greater than
                zero. Confirmed directly against a real SQL Server instance: both
                `TABLESAMPLE (-5 ROWS)` and `TABLESAMPLE (0 ROWS)` fail to compile with Msg 479
                ("The value or seed must be greater than 0"). Only literal constants are checked
                - a parameter or variable's value is not statically known.
                """,
            HowToFixIt: """
                Use a strictly positive literal row count.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A zero or negative literal TABLESAMPLE ROWS count never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer TABLESAMPLE (0 ROWS);
                        """,
                    NoncompliantExplanation: "A ROWS count of zero fails to compile with Msg 479 every time this statement runs.",
                    CompliantSql: """
                        SELECT * FROM dbo.Customer TABLESAMPLE (100 ROWS);
                        """,
                    CompliantExplanation: "A strictly positive ROWS count compiles."),
            ]);
    }
}

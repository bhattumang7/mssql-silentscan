using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.QueryAntiPattern;

internal static class TableVariablePspSkip
{
    public static string RuleId => SarifRuleCatalog.QueryAntiPatternRuleId(QueryAntiPatternFindingKind.TableVariablePspSkip);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: "A statement that reads a table-valued parameter as a table source - directly, or through a join - forfeits Parameter Sensitive Plan optimization for that statement at database compatibility level 170 or later: one cached plan must serve every parameter-value shape, even when the statement's own predicate is on a heavily skewed column. Oracle-confirmed this is scoped to the statement, not the module - a sibling statement in the same procedure that never reads the table-valued parameter still gets PSP plan variants normally, and a table-valued parameter that's declared but never read as a table source doesn't affect anything.",
        HowToFixIt: "Move the PSP-sensitive statement into a procedure or function without a table-valued parameter, or use a different data-passing design when PSP plan variants are required.",
        Examples:
        [
            new RuleDocExample(
                Title: "Reading a table-valued parameter as a table source loses PSP for that statement",
                NoncompliantSql: """
                    CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL PRIMARY KEY);
                    GO
                    CREATE PROCEDURE dbo.FindOrders
                        @CustomerId int,
                        @Ids dbo.IdList READONLY
                    AS
                    SELECT o.* FROM dbo.Orders o JOIN @Ids i ON i.Id = o.CustomerId WHERE o.CustomerId = @CustomerId;
                    """,
                NoncompliantExplanation: "This statement reads @Ids as a join source, so it cannot receive PSP plan variants even though its predicate on CustomerId would otherwise qualify.",
                CompliantSql: """
                    CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL PRIMARY KEY);
                    GO
                    CREATE PROCEDURE dbo.FindOrders
                        @CustomerId int,
                        @Ids dbo.IdList READONLY
                    AS
                    SELECT * FROM dbo.Orders WHERE CustomerId = @CustomerId;
                    """,
                CompliantExplanation: "This statement never reads @Ids as a table source, so PSP remains available for it even though the procedure still declares the table-valued parameter."),
        ]);
}

using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Join;

internal static class JoinPredicateEmptyWithWhereClause
{
    public static string RuleId => SarifRuleCatalog.CartesianJoinRuleId(CartesianJoinKind.JoinPredicateEmptyWithWhereClause);

    public static RuleDocContent Content { get; } = new(
        WhyItMatters: """
            An INNER JOIN's own ON predicate can be perfectly satisfiable in isolation - a normal
            equi-join between two tables' columns - and still be provably empty once the statement's
            WHERE clause is taken into account. When the WHERE clause constrains each side of the
            join to its own disjoint range of values (e.g. one side's column is pinned to 5, the
            other side's join column is pinned to 10), no pair of rows can ever satisfy the join
            predicate and the WHERE clause at the same time - the engine can prove the intersection
            of both sides' constraints is empty before it ever touches a row, and the join returns
            zero rows every time it runs, regardless of the tables' real data.

            This is the WHERE-clause-aware sibling of the always-false-inner-join-predicate family:
            that one finds the contradiction inside the ON predicate itself, this one finds it only
            by combining the ON predicate with the surrounding WHERE clause.
            """,
        HowToFixIt: """
            Correct the WHERE clause or the ON predicate - as written, the two provably never match
            at the same time.
            """,
        Examples:
        [
            new RuleDocExample(
                Title: "An equi-join whose WHERE clause pins each side to a disjoint constant",
                NoncompliantSql: """
                    CREATE TABLE dbo.A (X INT NOT NULL);
                    CREATE TABLE dbo.B (Y INT NOT NULL);

                    SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X = 5 AND b.Y = 10;
                    """,
                NoncompliantExplanation: "The ON predicate a.X = b.Y is satisfiable on its own, but the WHERE clause pins a.X to 5 and b.Y to 10 - since the join requires a.X = b.Y, and the WHERE clause requires a.X <> b.Y, no row pair can ever satisfy both, and this join returns zero rows every time.",
                CompliantSql: """
                    CREATE TABLE dbo.A (X INT NOT NULL);
                    CREATE TABLE dbo.B (Y INT NOT NULL);

                    SELECT * FROM dbo.A a INNER JOIN dbo.B b ON a.X = b.Y WHERE a.X = 5;
                    """,
                CompliantExplanation: "Constraining only one side leaves the join predicate free to match - a.X = b.Y and a.X = 5 together simply require b.Y = 5 too, which is satisfiable."),
        ]);
}

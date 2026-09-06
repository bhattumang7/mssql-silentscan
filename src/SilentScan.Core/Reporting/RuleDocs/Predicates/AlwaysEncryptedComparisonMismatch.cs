using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Core.Reporting.RuleDocs.Predicates;

internal static class AlwaysEncryptedComparisonMismatch
{
    internal static class LiteralOperand
    {
        public static string RuleId => SarifRuleCatalog.AlwaysEncryptedComparisonMismatchRuleId(AlwaysEncryptedComparisonMismatchKind.LiteralOperand);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                An Always Encrypted column's plaintext value never reaches the server, so the
                server cannot compare it against a plaintext literal either. Confirmed directly
                against a real SQL Server instance: comparing an encrypted column to a literal
                with `=`, `<>`, or `!=` fails to compile ("Operand type clash" or an
                "incompatible ... operator" error), because the server has no way to encrypt the
                literal to match.

                A `NULL` literal is exempt - `col = NULL` compiles and simply never matches. A
                parameter or variable source is never flagged - the client driver is expected to
                encrypt it appropriately before sending it.
                """,
            HowToFixIt: """
                Pass the value as a parameter from an Always Encrypted-enabled client connection
                instead of writing it as a literal, so the driver encrypts it to match the
                compared column's key and algorithm before the statement reaches the server.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A literal compared against an encrypted column never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer WHERE Ssn = '123-45-6789';
                        """,
                    NoncompliantExplanation: "Ssn is an Always Encrypted column - this WHERE clause fails to compile every time it runs, since the server cannot encrypt the literal to compare against.",
                    CompliantSql: """
                        -- from an Always Encrypted-enabled client connection
                        SELECT * FROM dbo.Customer WHERE Ssn = @ssn;
                        """,
                    CompliantExplanation: "The value arrives as a parameter, already encrypted by the client driver to match Ssn's own key and algorithm."),
            ]);
    }

    internal static class EncryptionStateMismatch
    {
        public static string RuleId => SarifRuleCatalog.AlwaysEncryptedComparisonMismatchRuleId(AlwaysEncryptedComparisonMismatchKind.EncryptionStateMismatch);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                Two Always Encrypted columns are only comparable when their encryption state
                matches exactly - encryption type and column encryption key both. Confirmed
                directly against a real SQL Server instance: comparing two columns with `=`,
                `<>`, or `!=` fails to compile whenever either differs - encrypted vs. plaintext
                (in either direction), a different encryption type (deterministic vs.
                randomized), or the same encryption type but a different column encryption key -
                even though both columns' declared types otherwise match exactly. Two columns
                that share the same deterministic encryption and key compare successfully with
                `=`/`<>`; that case is not flagged.

                Scoped to comparisons where both operands resolve to a statically known base
                column (through the query's own scope, including joins and aliases) - an
                expression, function call, or parameter/variable operand is never flagged, since
                its actual encrypted origin is not statically decidable for those shapes. Only
                the equality-family operators (`=`, `<>`, `!=`) are checked; ordering
                comparisons (`<`, `>`, `BETWEEN`, `LIKE`, ...) have their own, stricter
                enclave-related restrictions not covered by this rule.
                """,
            HowToFixIt: """
                Route the value through an Always Encrypted-enabled client - decrypt it and
                compare, or re-encrypt it to the other column's own key and algorithm - instead
                of comparing the encrypted values directly server-side.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "Comparing differently-encrypted columns never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer WHERE SsnRandomized = SsnDeterministic;
                        """,
                    NoncompliantExplanation: "SsnRandomized and SsnDeterministic use different encryption types - this WHERE clause fails to compile every time it runs, regardless of which side is which.",
                    CompliantSql: """
                        -- from an Always Encrypted-enabled client connection:
                        -- read and decrypt SsnDeterministic client-side, then compare
                        SELECT * FROM dbo.Customer WHERE SsnRandomized = @decryptedSsn;
                        """,
                    CompliantExplanation: "The client decrypts the source value and re-encrypts it to match SsnRandomized's own encryption type before the statement reaches the server."),
            ]);
    }
}

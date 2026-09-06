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
                `<>`, `!=`, `<`, `>`, `<=`, `>=`, or `BETWEEN` fails to compile whenever either
                differs - encrypted vs. plaintext (in either direction), a different encryption
                type (deterministic vs. randomized), or the same encryption type but a different
                column encryption key - even though both columns' declared types otherwise match
                exactly. Two columns that share the same encryption type and key are not flagged
                by this rule (they may still be flagged by this rule's sibling rules, which cover
                the deterministic-range and randomized-without-enclave restrictions that apply
                even when the encryption state itself matches).

                Scoped to comparisons where both operands resolve to a statically known base
                column (through the query's own scope, including joins and aliases) - an
                expression, function call, or parameter/variable operand is never flagged, since
                its actual encrypted origin is not statically decidable for those shapes. `LIKE`
                is not covered by this rule family - pattern matching against ciphertext has its
                own, separate restrictions this rule does not model.
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

    internal static class DeterministicRangeComparison
    {
        public static string RuleId => SarifRuleCatalog.AlwaysEncryptedComparisonMismatchRuleId(AlwaysEncryptedComparisonMismatchKind.DeterministicRangeComparison);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                Deterministic encryption only supports equality comparisons server-side, even
                between two columns that share the exact same column encryption key. Confirmed
                directly against a real SQL Server instance: comparing two matching deterministic
                columns with a range operator (`<`, `>`, `<=`, `>=`, `BETWEEN`) fails to compile
                ("Encryption scheme mismatch"), while the identical comparison with `=` or `<>`
                succeeds. This is unconditional - declaring `ENCLAVE_COMPUTATIONS` on the backing
                column master key does not lift the restriction for deterministic columns (it
                does for randomized columns; see this rule's sibling rule).
                """,
            HowToFixIt: """
                Route the value through an Always Encrypted-enabled client - decrypt both sides
                and compare - instead of comparing the encrypted values directly server-side with
                a range operator.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "A range comparison between matching deterministic columns never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer WHERE SsnCurrent > SsnPrevious;
                        """,
                    NoncompliantExplanation: "SsnCurrent and SsnPrevious share the same deterministic encryption and key, but > is a range operator - this WHERE clause fails to compile every time it runs.",
                    CompliantSql: """
                        -- from an Always Encrypted-enabled client connection:
                        -- decrypt both values client-side, then compare
                        SELECT * FROM dbo.Customer WHERE @decryptedCurrent > @decryptedPrevious;
                        """,
                    CompliantExplanation: "The client decrypts both values and compares them in plaintext before the statement reaches the server."),
            ]);
    }

    internal static class RandomizedWithoutEnclave
    {
        public static string RuleId => SarifRuleCatalog.AlwaysEncryptedComparisonMismatchRuleId(AlwaysEncryptedComparisonMismatchKind.RandomizedWithoutEnclave);

        public static RuleDocContent Content { get; } = new(
            WhyItMatters: """
                Randomized encryption produces different ciphertext for the same plaintext on
                every encryption, so comparing two randomized columns - even with `=` and even
                when they share the exact same column encryption key - needs a secure enclave to
                decrypt and compare server-side. Confirmed directly against a real SQL Server
                instance: comparing two matching randomized columns fails to compile ("Encryption
                scheme mismatch") when their column master key does not declare
                `ENCLAVE_COMPUTATIONS`; declaring it lifts the restriction for both equality and
                range comparisons on randomized columns (confirmed via the column master key's
                compile-time metadata alone, independent of whether a real enclave is
                provisioned).

                Only flagged when the column master key backing the comparison is itself visible
                to the scan and its DDL confirms no `ENCLAVE_COMPUTATIONS` declaration - an
                unresolvable column encryption key (for example, defined in a script outside this
                scan) is never flagged, to avoid a false positive on a key that might actually be
                enclave-enabled.
                """,
            HowToFixIt: """
                Route the value through an Always Encrypted-enabled client - decrypt both sides
                and compare - instead of comparing the encrypted values directly server-side, or
                provision a secure-enclave-enabled column master key for both columns.
                """,
            Examples:
            [
                new RuleDocExample(
                    Title: "Comparing matching randomized columns without an enclave never compiles",
                    NoncompliantSql: """
                        SELECT * FROM dbo.Customer WHERE Notes = ArchivedNotes;
                        """,
                    NoncompliantExplanation: "Notes and ArchivedNotes share the same randomized encryption and key, but their column master key has no ENCLAVE_COMPUTATIONS declared - this WHERE clause fails to compile every time it runs.",
                    CompliantSql: """
                        -- from an Always Encrypted-enabled client connection:
                        -- decrypt both values client-side, then compare
                        SELECT * FROM dbo.Customer WHERE @decryptedNotes = @decryptedArchivedNotes;
                        """,
                    CompliantExplanation: "The client decrypts both values and compares them in plaintext before the statement reaches the server."),
            ]);
    }
}

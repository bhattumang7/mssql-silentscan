using SilentScan.Core.Catalog;

namespace SilentScan.Core.Predicates;

public static class ScalarUdfInlineabilityClassifier
{
    private const int MaxInlineableTableReferenceCount = 49;
    private const int MinInliningCompatibilityLevel = 150;

    public static (ScalarUdfInlineability Inlineability, string? Blocker) Classify(ScalarUdfInfo info, int? compatibilityLevel, bool inliningSwitchedOff = false)
    {
        if (info.Kind == ScalarUdfKind.Clr)
        {
            return (ScalarUdfInlineability.NotInlineable, "CLR scalar UDFs are never inlined");
        }

        if (inliningSwitchedOff)
        {
            return (ScalarUdfInlineability.NotInlineable, "the database has switched the TSQL_SCALAR_UDF_INLINING database scoped configuration off");
        }

        if (compatibilityLevel is { } level && level < MinInliningCompatibilityLevel)
        {
            return (ScalarUdfInlineability.NotInlineable, $"database compatibility level {level} is below {MinInliningCompatibilityLevel}");
        }

        if (info.InlineabilityTableReferenceCount is { } tableReferenceCount && tableReferenceCount > MaxInlineableTableReferenceCount)
        {
            return (ScalarUdfInlineability.NotInlineable, $"references {tableReferenceCount} tables in its body, over the engine's {MaxInlineableTableReferenceCount}-table inlining limit");
        }

        if (info.EngineIsInlineable is true)
        {
            return (ScalarUdfInlineability.Inlineable, null);
        }

        if (info.EngineIsInlineable is false)
        {
            return (ScalarUdfInlineability.NotInlineable, info.InlineabilityBlocker);
        }

        return info.InlineabilityBlocker is { Length: > 0 } blocker
            ? (ScalarUdfInlineability.NotInlineable, blocker)
            : (ScalarUdfInlineability.Unknown, null);
    }
}

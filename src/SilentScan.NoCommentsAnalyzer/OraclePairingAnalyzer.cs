using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SilentScan.NoCommentsAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OraclePairingAnalyzer : DiagnosticAnalyzer
{
    public const string RawRuleTraitId = "SS0010";
    public const string UnpairedRuleId = "SS0011";
    public const string StaleBaselineId = "SS0012";
    public const string EngineSideUsesScannerId = "SS0013";

    private const string FindingRuleIdsMetadataName = "SilentScan.Core.Rules.FindingRuleIds";
    private const string RuleOracleMetadataName = "SilentScan.Tests.Support.RuleOracle";
    private const string RuleVerifierMetadataName = "SilentScan.Verify.Oracle.RuleVerifier`1";
    private const string TraitMetadataName = "Xunit.TraitAttribute";
    private const string BaselineFileName = "oracle-pairing-baseline.txt";
    private const string RuleTraitName = "Rule";

    private const string VerifierSupportAttributeName = "VerifierSupportAttribute";

    private static readonly string[] AllowedVerifierNamespaces =
    [
        "System",
        "Microsoft.Data",
    ];

    private static readonly string[] AllowedVerifierTypes =
    [
        "SilentScan.Verify.Oracle.Difference",
        "SilentScan.Verify.Oracle.Perturbation",
        "SilentScan.Verify.Oracle.PerturbationStep",
        "SilentScan.Verify.Oracle.RuleVerifier<TFinding>",
        "SilentScan.Verify.Oracle.TouchedObject",
        "SilentScan.Verify.Oracle.VerifierSupportAttribute",
        "SilentScan.Verify.SqlServerOptions",
    ];

    private static DiagnosticDescriptor Describe(string id, string title) => new(
        id,
        title,
        messageFormat: "{0}",
        category: "Testing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RawRuleTrait = Describe(RawRuleTraitId, "Rule trait must reference a FindingRuleIds constant");
    private static readonly DiagnosticDescriptor UnpairedRule = Describe(UnpairedRuleId, "Rule has no paired scanner-and-engine oracle test");
    private static readonly DiagnosticDescriptor StaleBaseline = Describe(StaleBaselineId, "Oracle pairing baseline entry is stale");
    private static readonly DiagnosticDescriptor EngineSideUsesScanner = Describe(EngineSideUsesScannerId, "Verifier may reference only its finding type, rule ids and the base library");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(RawRuleTrait, UnpairedRule, StaleBaseline, EngineSideUsesScanner);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var verifier = context.Compilation.GetTypeByMetadataName(RuleVerifierMetadataName);
        var ruleIds = context.Compilation.GetTypeByMetadataName(FindingRuleIdsMetadataName);
        if (verifier is not null && ruleIds is not null)
        {
            context.RegisterSyntaxNodeAction(
                nodeContext => CheckVerifierReferences(nodeContext, verifier, ruleIds),
                SyntaxKind.IdentifierName);
        }

        var oracle = context.Compilation.GetTypeByMetadataName(RuleOracleMetadataName);
        var trait = context.Compilation.GetTypeByMetadataName(TraitMetadataName);
        if (ruleIds is null || oracle is null || trait is null)
        {
            return;
        }

        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in ruleIds.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsConst && field.ConstantValue is string value)
            {
                constants[field.Name] = value;
            }
        }

        var nameByValue = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in constants)
        {
            nameByValue[pair.Value] = pair.Key;
        }

        var baseline = ReadBaseline(context.Options, context.CancellationToken);
        var paired = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        context.RegisterSymbolAction(
            symbolContext => CollectPaired(symbolContext, oracle, trait, nameByValue, paired),
            SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(
            nodeContext => CheckTraitArgument(nodeContext, ruleIds, trait),
            SyntaxKind.Attribute);
        context.RegisterCompilationEndAction(
            endContext => ReportCoverage(endContext, constants.Keys, baseline, paired.Keys));
    }

    private static HashSet<string> ReadBaseline(AnalyzerOptions options, System.Threading.CancellationToken cancellationToken)
    {
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in options.AdditionalFiles)
        {
            if (!file.Path.EndsWith(BaselineFileName, StringComparison.Ordinal))
            {
                continue;
            }

            var text = file.GetText(cancellationToken);
            if (text is null)
            {
                continue;
            }

            foreach (var line in text.Lines)
            {
                var entry = line.ToString().Trim();
                if (entry.Length > 0)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectPaired(
        SymbolAnalysisContext context,
        INamedTypeSymbol oracle,
        INamedTypeSymbol trait,
        Dictionary<string, string> nameByValue,
        ConcurrentDictionary<string, byte> paired)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || !DerivesFrom(type, oracle))
        {
            return;
        }

        var found = false;
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, trait)
                || attribute.ConstructorArguments.Length != 2
                || !string.Equals(attribute.ConstructorArguments[0].Value as string, RuleTraitName, StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments[1].Value is string value && nameByValue.TryGetValue(value, out var name))
            {
                paired[name] = 0;
                found = true;
            }
        }

        if (!found)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RawRuleTrait,
                type.Locations.FirstOrDefault(),
                type.Name + " derives from RuleOracle but declares no Rule trait naming a FindingRuleIds constant."));
        }
    }

    private static void CheckTraitArgument(SyntaxNodeAnalysisContext context, INamedTypeSymbol ruleIds, INamedTypeSymbol trait)
    {
        var attribute = (AttributeSyntax)context.Node;
        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count != 2)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol?.ContainingType is not { } attributeType
            || !SymbolEqualityComparer.Default.Equals(attributeType, trait))
        {
            return;
        }

        var name = context.SemanticModel.GetConstantValue(arguments.Value[0].Expression, context.CancellationToken);
        if (!name.HasValue || !string.Equals(name.Value as string, RuleTraitName, StringComparison.Ordinal))
        {
            return;
        }

        var valueExpression = arguments.Value[1].Expression;
        var symbol = context.SemanticModel.GetSymbolInfo(valueExpression, context.CancellationToken).Symbol;
        if (symbol is IFieldSymbol { IsConst: true } field && SymbolEqualityComparer.Default.Equals(field.ContainingType, ruleIds))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RawRuleTrait,
            valueExpression.GetLocation(),
            "Rule trait must reference a FindingRuleIds constant, not a literal or other expression."));
    }

    private static bool DerivesFromGeneric(INamedTypeSymbol type, INamedTypeSymbol genericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, genericBase))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? VerifiedFindingType(INamedTypeSymbol owner, INamedTypeSymbol genericBase)
    {
        for (var current = owner.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, genericBase) && current.TypeArguments.Length == 1)
            {
                return current.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static void CheckVerifierReferences(SyntaxNodeAnalysisContext context, INamedTypeSymbol verifier, INamedTypeSymbol ruleIds)
    {
        if (context.Node is IdentifierNameSyntax { IsVar: true })
        {
            return;
        }

        var declaration = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (declaration is null
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } owner
            || !(DerivesFromGeneric(owner, verifier) || IsVerifierSupport(owner)))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null || IsOwnOrNeutral(symbol, owner))
        {
            return;
        }

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type is null)
        {
            return;
        }

        var findingType = VerifiedFindingType(owner, verifier);
        if (IsAllowedForVerifier(type, findingType, ruleIds))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EngineSideUsesScanner,
            context.Node.GetLocation(),
            "Verifier " + owner.Name + " references " + symbol.ToDisplayString()
                + "; a verifier may use only its finding type, FindingRuleIds, the named verdict, base and options types, annotated support helpers, SqlClient and the base library, so its expected answer comes from SQL Server alone."));
    }

    private static bool IsVerifierSupport(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a => string.Equals(a.AttributeClass?.Name, VerifierSupportAttributeName, StringComparison.Ordinal));

    private static bool IsOwnOrNeutral(ISymbol symbol, INamedTypeSymbol owner)
    {
        switch (symbol.Kind)
        {
            case SymbolKind.Local:
            case SymbolKind.Parameter:
            case SymbolKind.RangeVariable:
            case SymbolKind.TypeParameter:
            case SymbolKind.Label:
            case SymbolKind.Namespace:
            case SymbolKind.Alias:
            case SymbolKind.ArrayType:
            case SymbolKind.PointerType:
            case SymbolKind.DynamicType:
            case SymbolKind.Discard:
                return true;
        }

        for (var current = symbol as INamedTypeSymbol ?? symbol.ContainingType; current is not null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, owner.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedForVerifier(INamedTypeSymbol type, INamedTypeSymbol? findingType, INamedTypeSymbol ruleIds)
    {
        var original = type.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(original, ruleIds)
            || IsVerifierSupport(original)
            || AllowedVerifierTypes.Contains(original.ToDisplayString(), StringComparer.Ordinal)
            || (findingType is not null && SymbolEqualityComparer.Default.Equals(original, findingType.OriginalDefinition)))
        {
            return true;
        }

        var display = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        foreach (var allowed in AllowedVerifierNamespaces)
        {
            if (string.Equals(display, allowed, StringComparison.Ordinal)
                || display.StartsWith(allowed + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportCoverage(
        CompilationAnalysisContext context,
        IEnumerable<string> constantNames,
        HashSet<string> baseline,
        IEnumerable<string> pairedNames)
    {
        var known = new HashSet<string>(constantNames, StringComparer.Ordinal);
        var paired = new HashSet<string>(pairedNames, StringComparer.Ordinal);

        foreach (var name in known.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!paired.Contains(name) && !baseline.Contains(name))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnpairedRule,
                    Location.None,
                    name + " has no RuleOracle test that checks the scanner and SQL Server on the same case, and is not in " + BaselineFileName + "."));
            }
        }

        foreach (var entry in baseline.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!known.Contains(entry))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaleBaseline,
                    Location.None,
                    entry + " in " + BaselineFileName + " names no FindingRuleIds constant."));
            }
            else if (paired.Contains(entry))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaleBaseline,
                    Location.None,
                    entry + " in " + BaselineFileName + " now has a RuleOracle test; remove it from the baseline."));
            }
        }
    }
}

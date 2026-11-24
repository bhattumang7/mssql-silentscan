using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Lineage;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

internal readonly record struct AssignmentSite(
    WriteLossTarget Target,
    string? TableQualifiedName,
    string Name,
    SqlType TargetType,
    SqlType? SourceType,
    ScalarExpression Source,
    ScopeChain? Scope = null,
    ModuleWalker? Walker = null);

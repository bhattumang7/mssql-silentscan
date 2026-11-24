using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Parsing;

public static class MethodCallTypeResolver
{
    public const string XmlRowsetMethodName = "nodes";

    private const int XmlValueUnsizedStringLength = 1;

    private static readonly SqlType HierarchyId = new(SqlTypeCategory.HierarchyId);

    private static readonly Dictionary<string, SqlType> HierarchyIdMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GetAncestor"] = HierarchyId,
        ["GetDescendant"] = HierarchyId,
        ["GetReparentedValue"] = HierarchyId,
        ["GetRoot"] = HierarchyId,
        ["Parse"] = HierarchyId,
        ["GetLevel"] = new SqlType(SqlTypeCategory.SmallInt),
        ["IsDescendantOf"] = new SqlType(SqlTypeCategory.Bit),
        ["ToString"] = new SqlType(SqlTypeCategory.NVarChar, Length: 4000),
    };

    public static bool HasMethodModel(SqlTypeCategory category) =>
        category is SqlTypeCategory.Xml or SqlTypeCategory.HierarchyId;

    public static SqlType? Resolve(SqlType? targetType, string methodName, IList<ScalarExpression> parameters, IReadOnlyDictionary<string, SqlType>? typeAliases) =>
        targetType?.Category switch
        {
            SqlTypeCategory.Xml => ResolveXmlMethod(methodName, parameters, typeAliases),
            SqlTypeCategory.HierarchyId => HierarchyIdMethods.GetValueOrDefault(methodName),
            _ => null,
        };

    public static SqlType? ResolveTypeName(SchemaObjectName typeName, IReadOnlyDictionary<string, SqlType>? typeAliases) =>
        SqlTypeReferenceResolver.Resolve(new UserDataTypeReference { Name = typeName }, columnCollation: null, typeAliases);

    private static SqlType? ResolveXmlMethod(string methodName, IList<ScalarExpression> parameters, IReadOnlyDictionary<string, SqlType>? typeAliases) =>
        methodName.ToUpperInvariant() switch
        {
            "VALUE" when parameters is [_, StringLiteral typeText] => ParseXmlValueType(typeText.Value, typeAliases),
            "QUERY" => new SqlType(SqlTypeCategory.Xml),
            "EXIST" => new SqlType(SqlTypeCategory.Bit),
            _ => null,
        };

    private static SqlType? ParseXmlValueType(string typeText, IReadOnlyDictionary<string, SqlType>? typeAliases)
    {
        var dataType = new TSql170Parser(initialQuotedIdentifiers: true).ParseScalarDataType(new StringReader(typeText), out var errors);
        return errors.Count == 0 && dataType is not null
            ? SqlTypeReferenceResolver.Resolve(dataType, columnCollation: null, typeAliases, XmlValueUnsizedStringLength)
            : null;
    }
}

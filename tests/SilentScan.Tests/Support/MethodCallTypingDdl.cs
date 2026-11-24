namespace SilentScan.Tests.Support;

internal static class MethodCallTypingDdl
{
    public const string Script = """
        CREATE TABLE dbo.Docs (Id INT NOT NULL PRIMARY KEY, Body XML NULL, Node HIERARCHYID NULL);
        GO
        CREATE FUNCTION dbo.fnTwice (@i INT) RETURNS BIGINT AS BEGIN RETURN @i * 2; END;
        GO
        CREATE VIEW dbo.vw_Methods AS
        SELECT d.Id AS Id,
               n.c.value('(@k)[1]', 'int') AS NodeInt,
               n.c.value('(@s)[1]', 'varchar') AS NodeUnsizedVarchar,
               n.c.value('(@d)[1]', 'decimal(9,2)') AS NodeDecimal,
               n.c.exist('@k') AS NodeExists,
               n.c.query('.') AS NodeQuery,
               d.Body.value('(/a/@k)[1]', 'nvarchar(20)') AS BodyValue,
               d.Node.GetLevel() AS Lvl,
               d.Node.GetAncestor(1) AS Ancestor,
               d.Node.ToString() AS NodeText,
               d.Node.IsDescendantOf(hierarchyid::GetRoot()) AS UnderRoot,
               Node.GetReparentedValue(hierarchyid::GetRoot(), hierarchyid::Parse('/1/')) AS Reparented,
               dbo.fnTwice(d.Id) AS Twice
        FROM dbo.Docs AS d
        CROSS APPLY d.Body.nodes('/a/b') AS n(c);
        GO
        CREATE SCHEMA Body;
        GO
        CREATE FUNCTION Body.nodes (@p NVARCHAR(10)) RETURNS TABLE AS RETURN SELECT CAST(1 AS INT) AS y;
        GO
        CREATE VIEW dbo.vw_UnqualifiedNodes AS
        SELECT x.y.value('(@k)[1]', 'bigint') AS K
        FROM dbo.Docs
        CROSS APPLY Body.nodes('/a') AS x(y);
        """;
}

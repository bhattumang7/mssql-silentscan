-- PLANTED: direct table, SQL_* collation, indexed column -> ScanForced, depth 0.
CREATE PROCEDURE dbo.usp_FindUserByName_Fires
    @DisplayName NVARCHAR(40)
AS
BEGIN
    SELECT UserId FROM dbo.Users WHERE DisplayName = @DisplayName;
END
GO

-- CLEAN TWIN: same predicate shape, varchar param matching the column's own family/collation.
CREATE PROCEDURE dbo.usp_FindUserByName_Clean
    @DisplayName VARCHAR(40)
AS
BEGIN
    SELECT UserId FROM dbo.Users WHERE DisplayName = @DisplayName;
END
GO

-- PLANTED: Windows collation, non-indexed column -> RangeSeek, depth 0.
CREATE PROCEDURE dbo.usp_FindUserByRegion_Fires
    @Region NVARCHAR(20)
AS
BEGIN
    SELECT UserId FROM dbo.Users WHERE Region = @Region;
END
GO

-- PLANTED: Tier-1 function-wrapped column (separate finding stream from the verdict engine).
CREATE PROCEDURE dbo.usp_FindUserByCreatedYear_Fires
    @Year INT
AS
BEGIN
    SELECT UserId FROM dbo.Users WHERE YEAR(CreatedAt) = @Year;
END
GO

-- CLEAN TWIN: sargable date-range rewrite of the same intent - must not fire Tier-1.
CREATE PROCEDURE dbo.usp_FindUserByCreatedYear_Clean
    @RangeStart DATETIME,
    @RangeEnd DATETIME
AS
BEGIN
    SELECT UserId FROM dbo.Users WHERE CreatedAt >= @RangeStart AND CreatedAt < @RangeEnd;
END
GO

-- PLANTED: predicate reaches the base column through two view layers -> ScanForced, depth 2.
CREATE PROCEDURE dbo.usp_FindOrderThroughViews_Fires
    @OrderCode NVARCHAR(20)
AS
BEGIN
    SELECT OrderId FROM dbo.vw_OrdersLevel2 WHERE OrderCode = @OrderCode;
END
GO

-- PLANTED: dynamic SQL, string literal only (the analyzable-in-principle case).
CREATE PROCEDURE dbo.usp_DynamicLiteral_Fires
AS
BEGIN
    EXEC('SELECT 1');
END
GO

-- PLANTED: dynamic SQL, variable-driven (the unanalyzable case).
CREATE PROCEDURE dbo.usp_DynamicVariable_Fires
    @Sql NVARCHAR(MAX)
AS
BEGIN
    EXEC(@Sql);
END
GO

-- CLEAN TWIN: an ordinary stored-procedure call is not dynamic SQL.
CREATE PROCEDURE dbo.usp_CallsAnotherProc_Clean
AS
BEGIN
    EXEC dbo.usp_FindUserByName_Clean @DisplayName = 'x';
END
GO

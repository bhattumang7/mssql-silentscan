CREATE TABLE dbo.Orders
(
    OrderId     INT             NOT NULL PRIMARY KEY,
    OrderCode   VARCHAR(20)     NOT NULL,
    CreatedAt   DATETIME2(3)    NOT NULL
);
GO

CREATE VIEW dbo.vw_OrdersLevel1
AS
    SELECT OrderId, OrderCode, CreatedAt
    FROM dbo.Orders;
GO

CREATE VIEW dbo.vw_OrdersLevel2
AS
    SELECT OrderId, OrderCode, CreatedAt
    FROM dbo.vw_OrdersLevel1;
GO

CREATE PROCEDURE dbo.usp_FindOrderByCode
    @OrderCode NVARCHAR(20)
AS
BEGIN
    SELECT OrderId, OrderCode, CreatedAt
    FROM dbo.vw_OrdersLevel2
    WHERE OrderCode = @OrderCode;
END
GO

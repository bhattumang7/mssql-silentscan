CREATE TABLE dbo.Orders
(
    OrderId     INT             NOT NULL PRIMARY KEY,
    OrderCode   VARCHAR(20)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    CreatedAt   DATETIME2(3)    NOT NULL
);
GO
CREATE INDEX IX_Orders_OrderCode ON dbo.Orders(OrderCode);
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

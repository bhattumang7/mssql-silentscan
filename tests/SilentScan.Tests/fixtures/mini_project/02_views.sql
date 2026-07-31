CREATE VIEW dbo.vw_OrdersLevel1 AS
    SELECT OrderId, OrderCode, UserId FROM dbo.Orders;
GO

CREATE VIEW dbo.vw_OrdersLevel2 AS
    SELECT OrderId, OrderCode, UserId FROM dbo.vw_OrdersLevel1;
GO

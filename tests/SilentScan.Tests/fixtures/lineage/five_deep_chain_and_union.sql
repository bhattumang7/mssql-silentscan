CREATE TABLE dbo.Orders
(
    OrderId   INT          NOT NULL PRIMARY KEY,
    OrderCode VARCHAR(20)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
);
GO

CREATE VIEW dbo.vw_L1 AS SELECT OrderId, OrderCode FROM dbo.Orders;
GO
CREATE VIEW dbo.vw_L2 AS SELECT OrderId, OrderCode FROM dbo.vw_L1;
GO
CREATE VIEW dbo.vw_L3 AS SELECT OrderId, OrderCode FROM dbo.vw_L2;
GO
CREATE VIEW dbo.vw_L4 AS SELECT OrderId, OrderCode FROM dbo.vw_L3;
GO
CREATE VIEW dbo.vw_L5 AS SELECT OrderId, OrderCode FROM dbo.vw_L4;
GO

-- A UNION ALL of differing-but-compatible branch types (int/bigint, varchar(20)/varchar(30)).
-- A same-type differing-COLLATION union was tried first and rejected outright by SQL Server
-- at CREATE VIEW time (Msg 457: collation conflict) - this is the deployable "mixed branch
-- types" case CLAUDE.md's UNION rule targets, verified against the real oracle.
CREATE TABLE dbo.OrdersUs (OrderId INT NOT NULL, OrderCode VARCHAR(20) NOT NULL);
GO
CREATE TABLE dbo.OrdersEu (OrderId BIGINT NOT NULL, OrderCode VARCHAR(30) NOT NULL);
GO
CREATE VIEW dbo.vw_AllOrders AS
    SELECT OrderId, OrderCode FROM dbo.OrdersUs
    UNION ALL
    SELECT OrderId, OrderCode FROM dbo.OrdersEu;
GO

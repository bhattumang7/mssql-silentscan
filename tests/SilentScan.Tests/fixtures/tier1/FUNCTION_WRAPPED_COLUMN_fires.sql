-- Source: "If You Can't Index It, It's Probably Not SARGable" - Brent Ozar
-- https://www.brentozar.com/archive/2018/03/cant-index-probably-not-sargable/
-- You can't build an index on YEAR(SomeDate); the engine must evaluate YEAR()
-- per row, so a predicate like this can't seek even with an index on SomeDate.
CREATE TABLE dbo.Orders
(
    OrderId   INT      NOT NULL PRIMARY KEY,
    SomeDate  DATETIME NOT NULL
);
GO
CREATE INDEX IX_Orders_SomeDate ON dbo.Orders(SomeDate);
GO

SELECT OrderId
FROM dbo.Orders
WHERE YEAR(SomeDate) = 2018;

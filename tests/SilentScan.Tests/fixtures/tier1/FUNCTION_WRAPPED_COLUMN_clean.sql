-- Near-miss for FUNCTION_WRAPPED_COLUMN: the sargable rewrite of the same
-- predicate from the Brent Ozar article this rule is sourced from
-- (https://www.brentozar.com/archive/2018/03/cant-index-probably-not-sargable/).
-- The date range form lets the engine seek on SomeDate directly - no function
-- wraps the column, so this must NOT fire.
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
WHERE SomeDate >= '20180101' AND SomeDate < '20190101';

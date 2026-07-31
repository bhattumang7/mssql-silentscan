-- Near-miss for CAST_CONVERT_ON_COLUMN: same range-scan intent as the forum
-- example this rule is sourced from, but rewritten sargable - the column is
-- compared directly, with the cast applied to the literal bounds instead.
-- Must NOT fire (only the column side matters per CLAUDE.md's direction rule).
CREATE TABLE dbo.Orders
(
    OrderId     INT      NOT NULL PRIMARY KEY,
    CreatedDate DATETIME NOT NULL
);
GO
CREATE INDEX IX_Orders_CreatedDate ON dbo.Orders(CreatedDate);
GO

SELECT OrderId
FROM dbo.Orders
WHERE CreatedDate >= CAST('2016-02-01' AS DATETIME)
  AND CreatedDate <  CAST('2016-02-09' AS DATETIME);

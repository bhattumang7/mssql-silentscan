-- Near-miss for COLUMN_ARITHMETIC: the sargable rewrite (move the arithmetic
-- to the literal side, per the same principle documented at
-- https://www.ibm.com/docs/en/ssw_ibm_i_75/rzajq/avoidarithexp.htm). Must NOT fire.
CREATE TABLE dbo.Products
(
    ProductId INT           NOT NULL PRIMARY KEY,
    UnitPrice DECIMAL(10,2) NOT NULL
);
GO
CREATE INDEX IX_Products_UnitPrice ON dbo.Products(UnitPrice);
GO

SELECT ProductId
FROM dbo.Products
WHERE UnitPrice < 2.975;

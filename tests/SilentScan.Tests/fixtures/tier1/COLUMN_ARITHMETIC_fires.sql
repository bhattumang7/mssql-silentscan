-- NOTE: hand-authored, not sourced from a real-world repo/incident. A targeted
-- search (see project research notes) did not turn up a real T-SQL .sql file
-- or blog repro with column arithmetic in a WHERE clause specifically - the
-- closest real reference found was cross-dialect (IBM i SQL docs,
-- "Avoid arithmetic expressions": https://www.ibm.com/docs/en/ssw_ibm_i_75/rzajq/avoidarithexp.htm,
-- WHERE SALARY > 15000*1.1 rewritten sargable as WHERE SALARY > 16500). The
-- sargability principle (arithmetic on the column defeats index seeks the
-- same way a function call does) is well-established and applies identically
-- to T-SQL; this fixture exists to test our own detector, not to assert a
-- specific real-world incident. Revisit if a real T-SQL example surfaces.
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
WHERE UnitPrice + 1 < 3.975;

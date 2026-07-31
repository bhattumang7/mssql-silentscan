-- Source: "Why is CAST using an Index Seek??" - SQLServerCentral Forums
-- https://www.sqlservercentral.com/forums/topic/why-is-cast-using-an-index-seek
-- A real forum repro of CAST(column AS type) inside a predicate. Note: the
-- thread's own finding is that a DATE cast is order-preserving and the
-- engine sometimes still seeks through it - Tier-1 flags this syntactically
-- regardless (CLAUDE.md "Known hard cases": computed/cast columns get an
-- explicit rule, not a silent pass); a later verdict pass refines this case
-- to RANGE_SEEK/SEEK_PRESERVED rather than SCAN_FORCED.
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
WHERE CAST(CreatedDate AS DATE) BETWEEN '2016-02-01' AND '2016-02-08';

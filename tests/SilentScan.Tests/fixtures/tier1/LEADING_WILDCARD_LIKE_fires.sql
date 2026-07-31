-- Source: "How to Make Leading Wildcard Searches Fast" - Brent Ozar
-- https://www.brentozar.com/archive/2025/08/how-to-make-leading-wildcard-searches-fast/
-- Matching rows for '%Ozar' are scattered throughout the whole B-tree, so
-- SQL Server can't seek to a starting point - full index scan required.
CREATE TABLE dbo.Users
(
    UserId      INT           NOT NULL PRIMARY KEY,
    DisplayName NVARCHAR(40)  NOT NULL
);
GO
CREATE INDEX IX_Users_DisplayName ON dbo.Users(DisplayName);
GO

SELECT UserId
FROM dbo.Users
WHERE DisplayName LIKE '%Ozar';

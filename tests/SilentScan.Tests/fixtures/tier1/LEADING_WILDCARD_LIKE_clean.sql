-- Near-miss for LEADING_WILDCARD_LIKE: a trailing-wildcard search over the
-- same column/article this rule is sourced from
-- (https://www.brentozar.com/archive/2025/08/how-to-make-leading-wildcard-searches-fast/).
-- A trailing wildcard keeps the prefix fixed, so the engine can seek to
-- 'Ozar' and scan forward - must NOT fire.
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
WHERE DisplayName LIKE 'Ozar%';

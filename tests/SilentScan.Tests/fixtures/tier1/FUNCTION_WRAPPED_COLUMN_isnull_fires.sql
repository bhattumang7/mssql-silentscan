-- Source: "Can Non-SARGable Predicates Ever Seek?" - Brent Ozar
-- https://www.brentozar.com/archive/2018/06/can-non-sargable-predicates-ever-seek/
-- ISNULL(Age, 0) = 0 forces a scan; the sargable rewrite is
-- "WHERE Age = 0 OR Age IS NULL", which restores two index seeks.
CREATE TABLE dbo.Users
(
    UserId INT NOT NULL PRIMARY KEY,
    Age    INT NULL
);
GO
CREATE INDEX IX_Users_Age ON dbo.Users(Age);
GO

SELECT UserId
FROM dbo.Users AS u
WHERE ISNULL(u.Age, 0) = 0;

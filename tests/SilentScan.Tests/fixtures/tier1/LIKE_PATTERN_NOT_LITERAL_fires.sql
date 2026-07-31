-- Tests our own analyzability boundary (CLAUDE.md: "LIKE @p marked
-- conditional"), not a specific real-world incident: when the LIKE pattern
-- is a parameter rather than a literal, whether it has a leading wildcard
-- can't be determined statically, so it must be flagged as unanalyzable
-- rather than silently passed as clean.
CREATE TABLE dbo.Users
(
    UserId      INT           NOT NULL PRIMARY KEY,
    DisplayName NVARCHAR(40)  NOT NULL
);
GO
CREATE INDEX IX_Users_DisplayName ON dbo.Users(DisplayName);
GO

CREATE PROCEDURE dbo.usp_FindUsersByNamePattern
    @Pattern NVARCHAR(40)
AS
BEGIN
    SELECT UserId
    FROM dbo.Users
    WHERE DisplayName LIKE @Pattern;
END
GO

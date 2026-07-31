CREATE TABLE dbo.Users
(
    UserId      INT             NOT NULL PRIMARY KEY,
    DisplayName VARCHAR(40)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    Region      VARCHAR(20)     COLLATE Latin1_General_CI_AS NOT NULL,
    CreatedAt   DATETIME        NOT NULL,
    Age         INT             NULL
);
GO
CREATE INDEX IX_Users_DisplayName ON dbo.Users(DisplayName);
GO

CREATE TABLE dbo.Orders
(
    OrderId   INT           NOT NULL PRIMARY KEY,
    OrderCode VARCHAR(20)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    UserId    INT           NOT NULL
);
GO
CREATE INDEX IX_Orders_OrderCode ON dbo.Orders(OrderCode);
GO

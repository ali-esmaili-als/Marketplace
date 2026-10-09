SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ProductReviews', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductReviews
    (
        Id BIGINT NOT NULL CONSTRAINT PK_ProductReviews PRIMARY KEY,
        ProductId BIGINT NOT NULL,
        CustomerId BIGINT NOT NULL,
        OrderId BIGINT NOT NULL,
        Rating TINYINT NOT NULL,
        Title NVARCHAR(150) NOT NULL,
        Body NVARCHAR(3000) NOT NULL,
        Status TINYINT NOT NULL CONSTRAINT DF_ProductReviews_Status DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL,
        ModeratedAtUtc DATETIME2(7) NULL,
        ModeratorUserId BIGINT NULL,
        ModerationNote NVARCHAR(1000) NULL,
        CONSTRAINT CK_ProductReviews_Rating CHECK (Rating BETWEEN 1 AND 5),
        CONSTRAINT CK_ProductReviews_Status CHECK (Status BETWEEN 1 AND 3),
        CONSTRAINT UQ_ProductReviews_Customer_Product UNIQUE (CustomerId, ProductId),
        CONSTRAINT FK_ProductReviews_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT FK_ProductReviews_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProductReviews_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id),
        CONSTRAINT FK_ProductReviews_Moderators FOREIGN KEY (ModeratorUserId) REFERENCES dbo.Users(Id)
    );

    CREATE INDEX IX_ProductReviews_Product_Status_Created
        ON dbo.ProductReviews(ProductId, Status, CreatedAtUtc DESC);
END;

COMMIT TRANSACTION;

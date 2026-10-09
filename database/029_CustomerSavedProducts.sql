SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SavedProducts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SavedProducts
    (
        Id BIGINT NOT NULL CONSTRAINT PK_SavedProducts PRIMARY KEY,
        CustomerId BIGINT NOT NULL,
        ProductId BIGINT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT CK_SavedProducts_Ids CHECK (CustomerId > 0 AND ProductId > 0),
        CONSTRAINT UQ_SavedProducts_Customer_Product UNIQUE (CustomerId, ProductId),
        CONSTRAINT FK_SavedProducts_Users FOREIGN KEY (CustomerId)
            REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SavedProducts_Products FOREIGN KEY (ProductId)
            REFERENCES dbo.Products(Id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.SavedProducts')
      AND name = N'IX_SavedProducts_Customer_CreatedAt'
)
    CREATE INDEX IX_SavedProducts_Customer_CreatedAt
        ON dbo.SavedProducts(CustomerId, CreatedAtUtc DESC);

COMMIT TRANSACTION;

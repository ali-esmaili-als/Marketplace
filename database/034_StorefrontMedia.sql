/*
 034 - Storefront media metadata.
 Apply after 033_StoreThemeCustomization.sql on an existing database.
 Uploaded bytes are stored under wwwroot/uploads/storefront; this table stores public URLs
 and ownership metadata. Only JPEG, PNG and WebP are accepted by the API.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.StorefrontMedia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StorefrontMedia
    (
        Id BIGINT NOT NULL CONSTRAINT PK_StorefrontMedia PRIMARY KEY,
        StoreId BIGINT NOT NULL,
        ProductId BIGINT NULL,
        Kind NVARCHAR(20) NOT NULL,
        Url NVARCHAR(500) NOT NULL,
        ContentType NVARCHAR(50) NOT NULL,
        AltText NVARCHAR(250) NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_StorefrontMedia_SortOrder DEFAULT(0),
        IsActive BIT NOT NULL CONSTRAINT DF_StorefrontMedia_IsActive DEFAULT(1),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_StorefrontMedia_CreatedAtUtc DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT CK_StorefrontMedia_Kind CHECK
            ((Kind IN (N'logo',N'banner') AND ProductId IS NULL) OR (Kind=N'product' AND ProductId IS NOT NULL)),
        CONSTRAINT CK_StorefrontMedia_SortOrder CHECK (SortOrder >= 0),
        CONSTRAINT CK_StorefrontMedia_ContentType CHECK (ContentType IN (N'image/jpeg',N'image/png',N'image/webp')),
        CONSTRAINT FK_StorefrontMedia_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id) ON DELETE CASCADE,
        CONSTRAINT FK_StorefrontMedia_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id)
    );
    CREATE INDEX IX_StorefrontMedia_Store_Kind_Active_Order
        ON dbo.StorefrontMedia(StoreId, Kind, IsActive, SortOrder);
    CREATE INDEX IX_StorefrontMedia_Product_Active_Order
        ON dbo.StorefrontMedia(ProductId, IsActive, SortOrder);
END;

COMMIT TRANSACTION;

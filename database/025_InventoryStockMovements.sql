SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.InventoryStockMovements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryStockMovements
    (
        Id BIGINT NOT NULL CONSTRAINT PK_InventoryStockMovements PRIMARY KEY,
        ProductVariantId BIGINT NOT NULL,
        SellerId BIGINT NOT NULL,
        PreviousStockQuantity BIGINT NOT NULL,
        NewStockQuantity BIGINT NOT NULL,
        Reason NVARCHAR(500) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT CK_InventoryStockMovements_Quantities
            CHECK (PreviousStockQuantity >= 0 AND NewStockQuantity >= 0),
        CONSTRAINT CK_InventoryStockMovements_Reason CHECK (LEN(Reason) > 0),
        CONSTRAINT FK_InventoryStockMovements_Variants
            FOREIGN KEY (ProductVariantId) REFERENCES dbo.ProductVariants(Id),
        CONSTRAINT FK_InventoryStockMovements_Sellers
            FOREIGN KEY (SellerId) REFERENCES dbo.Sellers(Id)
    );

    CREATE INDEX IX_InventoryStockMovements_Variant_Created
        ON dbo.InventoryStockMovements(ProductVariantId, CreatedAtUtc DESC, Id DESC);
    CREATE INDEX IX_InventoryStockMovements_Seller_Created
        ON dbo.InventoryStockMovements(SellerId, CreatedAtUtc DESC);
END;

COMMIT TRANSACTION;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.InventoryItems', N'LowStockThreshold') IS NULL
BEGIN
    ALTER TABLE dbo.InventoryItems
        ADD LowStockThreshold BIGINT NOT NULL
            CONSTRAINT DF_InventoryItems_LowStockThreshold DEFAULT (5) WITH VALUES;

    ALTER TABLE dbo.InventoryItems
        ADD CONSTRAINT CK_InventoryItems_LowStockThreshold
            CHECK (LowStockThreshold >= 0 AND LowStockThreshold <= 1000000000);
END;

COMMIT TRANSACTION;

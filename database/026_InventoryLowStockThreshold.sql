SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.InventoryItems', N'LowStockThreshold') IS NULL
BEGIN
    ALTER TABLE dbo.InventoryItems
        ADD LowStockThreshold BIGINT NOT NULL
            CONSTRAINT DF_InventoryItems_LowStockThreshold DEFAULT (5) WITH VALUES;
END;

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.InventoryItems')
      AND name = N'CK_InventoryItems_Qty'
)
    ALTER TABLE dbo.InventoryItems DROP CONSTRAINT CK_InventoryItems_Qty;

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.InventoryItems')
      AND name = N'CK_InventoryItems_LowStockThreshold'
)
    ALTER TABLE dbo.InventoryItems DROP CONSTRAINT CK_InventoryItems_LowStockThreshold;

ALTER TABLE dbo.InventoryItems
    ADD CONSTRAINT CK_InventoryItems_Qty
        CHECK
        (
            StockQuantity >= 0
            AND ReservedQuantity >= 0
            AND ReservedQuantity <= StockQuantity
            AND LowStockThreshold >= 0
            AND LowStockThreshold <= 1000000000
        );

COMMIT TRANSACTION;

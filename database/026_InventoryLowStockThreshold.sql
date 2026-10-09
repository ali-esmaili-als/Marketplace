SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.InventoryItems', N'LowStockThreshold') IS NULL
BEGIN
    ALTER TABLE dbo.InventoryItems
        ADD LowStockThreshold BIGINT NOT NULL
            CONSTRAINT DF_InventoryItems_LowStockThreshold DEFAULT (5) WITH VALUES;
END;

-- Repair legacy databases where the column exists but has no default constraint.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.InventoryItems')
      AND c.name = N'LowStockThreshold'
)
BEGIN
    ALTER TABLE dbo.InventoryItems
        ADD CONSTRAINT DF_InventoryItems_LowStockThreshold
            DEFAULT (5) FOR LowStockThreshold;
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

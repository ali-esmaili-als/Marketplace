/*
    Marketplace migration 018 - Refund ledger identity
    Safe to run against an existing SQL Server database.
    Fresh databases created from Marketplace_Complete.sql already contain RefundId,
    its FK, and the filtered unique index; this migration is intentionally idempotent.
    Historical ledger rows are not backfilled because retry history cannot be inferred safely.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.BalanceTransactions', N'U') IS NULL
        THROW 51018, 'Migration 018 requires dbo.BalanceTransactions.', 1;

    IF OBJECT_ID(N'dbo.Refunds', N'U') IS NULL
        THROW 51018, 'Migration 018 requires dbo.Refunds.', 1;

    IF COL_LENGTH(N'dbo.BalanceTransactions', N'RefundId') IS NULL
        ALTER TABLE dbo.BalanceTransactions ADD RefundId BIGINT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_BalanceTransactions_Refunds'
          AND parent_object_id = OBJECT_ID(N'dbo.BalanceTransactions')
    )
        ALTER TABLE dbo.BalanceTransactions WITH CHECK
            ADD CONSTRAINT FK_BalanceTransactions_Refunds
            FOREIGN KEY (RefundId) REFERENCES dbo.Refunds(Id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_BalanceTransactions_RefundId'
          AND object_id = OBJECT_ID(N'dbo.BalanceTransactions')
    )
        CREATE UNIQUE INDEX UX_BalanceTransactions_RefundId
            ON dbo.BalanceTransactions(RefundId)
            WHERE RefundId IS NOT NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

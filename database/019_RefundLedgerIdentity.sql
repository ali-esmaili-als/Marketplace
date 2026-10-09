/* 019 - Link each refund ledger posting to the exact refund for safe retry and reconciliation. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.BalanceTransactions', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Refunds', N'U') IS NULL
        THROW 51901, 'Cannot apply migration 019: BalanceTransactions or Refunds table is missing.', 1;

    IF COL_LENGTH(N'dbo.BalanceTransactions', N'RefundId') IS NULL
        ALTER TABLE dbo.BalanceTransactions ADD RefundId BIGINT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.BalanceTransactions')
          AND name = N'FK_BalanceTransactions_Refunds'
    )
        ALTER TABLE dbo.BalanceTransactions WITH CHECK
            ADD CONSTRAINT FK_BalanceTransactions_Refunds
            FOREIGN KEY (RefundId) REFERENCES dbo.Refunds(Id);

    IF EXISTS
    (
        SELECT 1 FROM dbo.BalanceTransactions
        WHERE RefundId IS NOT NULL
        GROUP BY RefundId
        HAVING COUNT_BIG(*) > 1
    )
        THROW 51902, 'Cannot apply migration 019: duplicate refund ledger identities exist.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BalanceTransactions')
          AND name = N'UX_BalanceTransactions_RefundId'
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

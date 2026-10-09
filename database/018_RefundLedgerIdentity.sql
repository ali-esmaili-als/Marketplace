/* 018 - Link each refund ledger posting to the exact refund for safe retry and reconciliation. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.BalanceTransactions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Refunds', N'U') IS NULL
    THROW 51801, 'Cannot apply patch 018: BalanceTransactions or Refunds table is missing.', 1;

IF COL_LENGTH(N'dbo.BalanceTransactions', N'RefundId') IS NULL
    ALTER TABLE dbo.BalanceTransactions ADD RefundId BIGINT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.BalanceTransactions')
      AND name = N'FK_BalanceTransactions_Refunds'
)
    ALTER TABLE dbo.BalanceTransactions
        ADD CONSTRAINT FK_BalanceTransactions_Refunds
        FOREIGN KEY (RefundId) REFERENCES dbo.Refunds(Id);

IF EXISTS
(
    SELECT 1 FROM dbo.BalanceTransactions
    WHERE RefundId IS NOT NULL
    GROUP BY RefundId
    HAVING COUNT_BIG(*) > 1
)
    THROW 51802, 'Cannot apply patch 018: duplicate refund ledger identities exist.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BalanceTransactions')
      AND name = N'UX_BalanceTransactions_RefundId'
)
    CREATE UNIQUE INDEX UX_BalanceTransactions_RefundId
        ON dbo.BalanceTransactions(RefundId)
        WHERE RefundId IS NOT NULL;

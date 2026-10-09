/*
 015 - Database-enforced idempotency for payment authorities and active refunds.
 The filtered indexes are part of the fresh bootstrap schema too. Before applying to
 an existing database, inspect duplicates rather than deleting or rewriting financial rows.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Refunds', N'U') IS NULL
    THROW 51501, 'Cannot apply patch 015: PaymentTransactions or Refunds table is missing.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.PaymentTransactions
    WHERE Authority IS NOT NULL
    GROUP BY Provider, Authority
    HAVING COUNT_BIG(*) > 1
)
    THROW 51502, 'Cannot apply patch 015: duplicate provider/authority pairs exist in PaymentTransactions.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.Refunds
    WHERE Status < 4
    GROUP BY OrderId
    HAVING COUNT_BIG(*) > 1
)
    THROW 51503, 'Cannot apply patch 015: multiple active refunds exist for an order.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.PaymentTransactions')
      AND name=N'UX_PaymentTransactions_Provider_Authority'
)
    CREATE UNIQUE INDEX UX_PaymentTransactions_Provider_Authority
        ON dbo.PaymentTransactions(Provider, Authority)
        WHERE Authority IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.Refunds')
      AND name=N'UX_Refunds_OneActivePerOrder'
)
    CREATE UNIQUE INDEX UX_Refunds_OneActivePerOrder
        ON dbo.Refunds(OrderId)
        WHERE Status < 4;

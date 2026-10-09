/*
 014 - Enforce persisted status enum ranges for the core payment lifecycle.
 PaymentStatus: 1..8; PaymentTransactionStatus: 1..4;
 RefundStatus: 1..6; SettlementStatus: 1..6; OrderStatus: 1..10.
 Fresh databases receive the same trusted constraints from Marketplace_Complete.sql.
 Existing invalid values must be reviewed before this patch is applied; the script
 intentionally fails rather than silently rewriting financial history.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Payments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Refunds', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Settlements', N'U') IS NULL
    THROW 51401, 'Cannot apply patch 014: one or more required lifecycle tables are missing. Apply the Marketplace schema first.', 1;

IF EXISTS (SELECT 1 FROM dbo.Orders WHERE Status NOT BETWEEN 1 AND 10)
    THROW 51402, 'Cannot apply patch 014: invalid order statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.Payments WHERE Status NOT BETWEEN 1 AND 8)
    THROW 51403, 'Cannot apply patch 014: invalid payment statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE Status NOT BETWEEN 1 AND 4)
    THROW 51404, 'Cannot apply patch 014: invalid payment transaction statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.Refunds WHERE Status NOT BETWEEN 1 AND 6)
    THROW 51405, 'Cannot apply patch 014: invalid refund statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.Settlements WHERE Status NOT BETWEEN 1 AND 6)
    THROW 51406, 'Cannot apply patch 014: invalid settlement statuses exist.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Orders') AND name=N'CK_Orders_Status')
    ALTER TABLE dbo.Orders WITH CHECK ADD CONSTRAINT CK_Orders_Status CHECK(Status BETWEEN 1 AND 10);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Payments') AND name=N'CK_Payments_Status')
    ALTER TABLE dbo.Payments WITH CHECK ADD CONSTRAINT CK_Payments_Status CHECK(Status BETWEEN 1 AND 8);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.PaymentTransactions') AND name=N'CK_PaymentTransactions_Status')
    ALTER TABLE dbo.PaymentTransactions WITH CHECK ADD CONSTRAINT CK_PaymentTransactions_Status CHECK(Status BETWEEN 1 AND 4);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Refunds') AND name=N'CK_Refunds_Status')
    ALTER TABLE dbo.Refunds WITH CHECK ADD CONSTRAINT CK_Refunds_Status CHECK(Status BETWEEN 1 AND 6);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Settlements') AND name=N'CK_Settlements_Status')
    ALTER TABLE dbo.Settlements WITH CHECK ADD CONSTRAINT CK_Settlements_Status CHECK(Status BETWEEN 1 AND 6);

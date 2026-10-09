/*
 013 - Validate lifecycle enum values at the SQL Server boundary for existing databases.
 Values mirror the domain enums:
   SellerBalanceHold: Active=1, Released=2, Consumed=3
   InventoryReservation: Active=1, Consumed=2, Released=3, Expired=4
   Delivery: Pending=1, Ready=2, Delivered=3, Expired=4, Cancelled=5
   BalanceTransactionType: 1..14; BalanceBucket: Available=1, Pending=2,
     Blocked=3, ReservedForSettlement=4, Liability=5
 Fresh databases receive these checks from Marketplace_Complete.sql.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SellerBalanceHolds', N'U') IS NULL
   OR OBJECT_ID(N'dbo.InventoryReservations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Deliveries', N'U') IS NULL
   OR OBJECT_ID(N'dbo.BalanceTransactions', N'U') IS NULL
    THROW 51301, 'Cannot apply patch 013: one or more required lifecycle tables are missing. Apply the Marketplace schema first.', 1;

IF EXISTS (SELECT 1 FROM dbo.SellerBalanceHolds WHERE Status NOT BETWEEN 1 AND 3)
    THROW 51302, 'Cannot apply patch 013: invalid seller balance hold statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.InventoryReservations WHERE Status NOT BETWEEN 1 AND 4)
    THROW 51303, 'Cannot apply patch 013: invalid inventory reservation statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.Deliveries WHERE Status NOT BETWEEN 1 AND 5)
    THROW 51304, 'Cannot apply patch 013: invalid delivery statuses exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.BalanceTransactions WHERE Type NOT BETWEEN 1 AND 14)
    THROW 51305, 'Cannot apply patch 013: invalid balance transaction types exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.BalanceTransactions WHERE Bucket NOT BETWEEN 1 AND 5)
    THROW 51306, 'Cannot apply patch 013: invalid balance transaction buckets exist.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.SellerBalanceHolds') AND name=N'CK_SellerBalanceHolds_Status')
    ALTER TABLE dbo.SellerBalanceHolds WITH CHECK ADD CONSTRAINT CK_SellerBalanceHolds_Status CHECK(Status BETWEEN 1 AND 3);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.InventoryReservations') AND name=N'CK_InventoryReservations_Status')
    ALTER TABLE dbo.InventoryReservations WITH CHECK ADD CONSTRAINT CK_InventoryReservations_Status CHECK(Status BETWEEN 1 AND 4);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Deliveries') AND name=N'CK_Deliveries_Status')
    ALTER TABLE dbo.Deliveries WITH CHECK ADD CONSTRAINT CK_Deliveries_Status CHECK(Status BETWEEN 1 AND 5);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.BalanceTransactions') AND name=N'CK_BalanceTransactions_Type')
    ALTER TABLE dbo.BalanceTransactions WITH CHECK ADD CONSTRAINT CK_BalanceTransactions_Type CHECK(Type BETWEEN 1 AND 14);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.BalanceTransactions') AND name=N'CK_BalanceTransactions_Bucket')
    ALTER TABLE dbo.BalanceTransactions WITH CHECK ADD CONSTRAINT CK_BalanceTransactions_Bucket CHECK(Bucket BETWEEN 1 AND 5);

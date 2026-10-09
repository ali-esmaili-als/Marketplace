/*
 012 - Enforce the known ComplaintStatus enum range on an existing database.
 Values: Open=1, UnderReview=2, CustomerWon=3, SellerWon=4, Cancelled=5, Closed=6.
 Fresh databases receive the same constraint from Marketplace_Complete.sql.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Complaints', N'U') IS NULL
    THROW 51201, 'Cannot apply patch 012: dbo.Complaints does not exist. Apply the Marketplace schema first.', 1;

IF EXISTS (SELECT 1 FROM dbo.Complaints WHERE Status NOT BETWEEN 1 AND 6)
    THROW 51202, 'Cannot apply patch 012: invalid complaint status values exist. Review those rows before retrying.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Complaints')
      AND name = N'CK_Complaints_Status'
)
    ALTER TABLE dbo.Complaints WITH CHECK
        ADD CONSTRAINT CK_Complaints_Status CHECK (Status BETWEEN 1 AND 6);

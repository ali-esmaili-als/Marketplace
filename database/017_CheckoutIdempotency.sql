SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.Orders', N'RequestKey') IS NULL
    ALTER TABLE dbo.Orders ADD RequestKey NVARCHAR(64) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Orders') AND name=N'UX_Orders_Customer_RequestKey')
    CREATE UNIQUE INDEX UX_Orders_Customer_RequestKey ON dbo.Orders(CustomerId,RequestKey) WHERE RequestKey IS NOT NULL;
IF COL_LENGTH(N'dbo.Payments', N'RedirectUrl') IS NULL
    ALTER TABLE dbo.Payments ADD RedirectUrl NVARCHAR(2048) NULL;
COMMIT TRANSACTION;

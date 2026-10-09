/* 016 - Idempotency key for seller settlement requests. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Settlements', N'U') IS NULL THROW 51601, 'Cannot apply patch 016: dbo.Settlements table is missing.', 1;
IF COL_LENGTH(N'dbo.Settlements', N'RequestKey') IS NULL ALTER TABLE dbo.Settlements ADD RequestKey NVARCHAR(64) NULL;
IF EXISTS (SELECT 1 FROM dbo.Settlements WHERE RequestKey IS NOT NULL GROUP BY SellerId, RequestKey HAVING COUNT_BIG(*) > 1) THROW 51602, 'Cannot apply patch 016: duplicate seller settlement request keys exist.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Settlements') AND name=N'UX_Settlements_Seller_RequestKey') CREATE UNIQUE INDEX UX_Settlements_Seller_RequestKey ON dbo.Settlements(SellerId, RequestKey) WHERE RequestKey IS NOT NULL;

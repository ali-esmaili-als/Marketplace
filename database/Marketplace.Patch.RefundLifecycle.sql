/*
  Marketplace refund lifecycle patch.
  Safe to run repeatedly.
*/
IF COL_LENGTH(N'dbo.Refunds', N'GatewayRefundReference') IS NULL
BEGIN
    ALTER TABLE dbo.Refunds
        ADD GatewayRefundReference varchar(200) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Refunds_PaymentId_Status'
      AND object_id = OBJECT_ID(N'dbo.Refunds')
)
BEGIN
    CREATE INDEX IX_Refunds_PaymentId_Status
        ON dbo.Refunds(PaymentId, Status, CreatedAtUtc);
END
GO

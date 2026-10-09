/*
 Marketplace incremental patch 005
 Adds an audit trail for manual refund reconciliation.
 Safe to run against an existing Marketplace database; repeatable.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.RefundReconciliationAudits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefundReconciliationAudits(
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefundReconciliationAudits PRIMARY KEY,
        RefundId BIGINT NOT NULL,
        AdminUserId BIGINT NOT NULL,
        TransferCompleted BIT NOT NULL,
        Note NVARCHAR(2000) NOT NULL,
        BankReference NVARCHAR(200) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_RefundReconciliationAudits_Refunds FOREIGN KEY(RefundId) REFERENCES dbo.Refunds(Id),
        CONSTRAINT CK_RefundReconciliationAudits_Note CHECK(LEN(LTRIM(RTRIM(Note)))>0),
        CONSTRAINT CK_RefundReconciliationAudits_BankReference CHECK(TransferCompleted=0 OR LEN(LTRIM(RTRIM(ISNULL(BankReference,N''))))>0)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RefundReconciliationAudits') AND name=N'IX_RefundReconciliationAudits_Refund_Created')
    CREATE INDEX IX_RefundReconciliationAudits_Refund_Created ON dbo.RefundReconciliationAudits(RefundId,CreatedAtUtc);

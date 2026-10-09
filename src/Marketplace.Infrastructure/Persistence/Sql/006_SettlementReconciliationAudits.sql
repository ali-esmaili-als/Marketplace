/*
 Marketplace incremental patch 006
 Adds a durable audit trail for manual seller-settlement reconciliation.
 Safe to run repeatedly against an existing Marketplace database.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SettlementReconciliationAudits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SettlementReconciliationAudits(
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SettlementReconciliationAudits PRIMARY KEY,
        SettlementId BIGINT NOT NULL,
        AdminUserId BIGINT NOT NULL,
        TransferCompleted BIT NOT NULL,
        Note NVARCHAR(2000) NOT NULL,
        BankReference NVARCHAR(200) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_SettlementReconciliationAudits_Settlements FOREIGN KEY(SettlementId) REFERENCES dbo.Settlements(Id),
        CONSTRAINT CK_SettlementReconciliationAudits_Note CHECK(LEN(LTRIM(RTRIM(Note)))>0),
        CONSTRAINT CK_SettlementReconciliationAudits_BankReference CHECK(TransferCompleted=0 OR LEN(LTRIM(RTRIM(ISNULL(BankReference,N''))))>0)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SettlementReconciliationAudits') AND name=N'IX_SettlementReconciliationAudits_Settlement_Created')
    CREATE INDEX IX_SettlementReconciliationAudits_Settlement_Created
        ON dbo.SettlementReconciliationAudits(SettlementId,CreatedAtUtc);

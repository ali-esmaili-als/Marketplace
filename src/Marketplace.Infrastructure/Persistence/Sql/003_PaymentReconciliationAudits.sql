IF OBJECT_ID(N'dbo.PaymentReconciliationAudits',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentReconciliationAudits
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentReconciliationAudits PRIMARY KEY,
        PaymentId BIGINT NOT NULL,
        AdminUserId BIGINT NOT NULL,
        Action NVARCHAR(30) NOT NULL,
        Note NVARCHAR(2000) NOT NULL,
        BankReference NVARCHAR(200) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_PaymentReconciliationAudits_Payments FOREIGN KEY (PaymentId)
            REFERENCES dbo.Payments(Id),
        CONSTRAINT CK_PaymentReconciliationAudits_Action CHECK (Action IN (N'RefundCompleted',N'KeepOpen'))
    );
    CREATE INDEX IX_PaymentReconciliationAudits_PaymentId_CreatedAtUtc
        ON dbo.PaymentReconciliationAudits(PaymentId, CreatedAtUtc);
END
GO

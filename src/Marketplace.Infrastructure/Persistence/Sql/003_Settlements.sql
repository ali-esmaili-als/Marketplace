IF OBJECT_ID(N'dbo.Settlements',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Settlements
    (
        Id BIGINT NOT NULL CONSTRAINT PK_Settlements PRIMARY KEY,
        SellerId BIGINT NOT NULL,
        AmountIRR BIGINT NOT NULL,
        Status TINYINT NOT NULL,
        BankAccountId BIGINT NOT NULL,
        BankNameSnapshot NVARCHAR(150) NOT NULL,
        IbanSnapshot NVARCHAR(34) NOT NULL,
        AccountHolderNameSnapshot NVARCHAR(250) NOT NULL,
        Reference NVARCHAR(200) NULL,
        FailureReason NVARCHAR(1000) NULL,
        RequestedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL
    );
    CREATE INDEX IX_Settlements_Seller_Status ON dbo.Settlements(SellerId,Status);
    CREATE INDEX IX_Settlements_Status_RequestedAt ON dbo.Settlements(Status,RequestedAtUtc);
END
GO
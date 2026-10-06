IF COL_LENGTH(N'dbo.BalanceTransactions',N'Bucket') IS NULL
BEGIN
 ALTER TABLE dbo.BalanceTransactions ADD Bucket TINYINT NOT NULL CONSTRAINT DF_BalanceTransactions_Bucket DEFAULT(1);
END
GO
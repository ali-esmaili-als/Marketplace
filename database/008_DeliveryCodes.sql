IF OBJECT_ID(N'dbo.DeliveryCodes',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DeliveryCodes(
  Id BIGINT NOT NULL CONSTRAINT PK_DeliveryCodes PRIMARY KEY,
  OrderId BIGINT NOT NULL,
  CodeHash BINARY(32) NOT NULL,
  ExpiresAtUtc DATETIME2 NOT NULL,
  IssuedAtUtc DATETIME2 NOT NULL,
  UsedAtUtc DATETIME2 NULL,
  FailedAttempts INT NOT NULL CONSTRAINT DF_DeliveryCodes_FailedAttempts DEFAULT(0),
  CONSTRAINT FK_DeliveryCodes_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
  CONSTRAINT UQ_DeliveryCodes_Order UNIQUE(OrderId)
 );
 CREATE INDEX IX_DeliveryCodes_Expiry ON dbo.DeliveryCodes(ExpiresAtUtc,UsedAtUtc);
END
GO
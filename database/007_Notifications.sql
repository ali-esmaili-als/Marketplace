IF OBJECT_ID(N'dbo.Notifications',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.Notifications(
  Id BIGINT NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
  UserId BIGINT NOT NULL,
  Channel TINYINT NOT NULL,
  Status TINYINT NOT NULL,
  Title NVARCHAR(250) NOT NULL,
  Body NVARCHAR(4000) NOT NULL,
  ReferenceType NVARCHAR(100) NULL,
  ReferenceId BIGINT NULL,
  CreatedAtUtc DATETIME2 NOT NULL,
  SentAtUtc DATETIME2 NULL,
  ReadAtUtc DATETIME2 NULL,
  CONSTRAINT FK_Notifications_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
 );
 CREATE INDEX IX_Notifications_User_Status_Created ON dbo.Notifications(UserId,Status,CreatedAtUtc DESC);
END
GO
IF OBJECT_ID(N'dbo.AdminAuditEvents',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.AdminAuditEvents
 (
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdminAuditEvents PRIMARY KEY,
  ActorUserId BIGINT NOT NULL,
  Action NVARCHAR(100) NOT NULL,
  EntityType NVARCHAR(100) NOT NULL,
  EntityKey NVARCHAR(200) NOT NULL,
  DetailsJson NVARCHAR(2000) NOT NULL CONSTRAINT DF_AdminAuditEvents_DetailsJson DEFAULT(N'{}'),
  CorrelationId NVARCHAR(100) NULL,
  CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AdminAuditEvents_CreatedAtUtc DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_AdminAuditEvents_Users FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
  CONSTRAINT CK_AdminAuditEvents_DetailsJson CHECK (ISJSON(DetailsJson)=1)
 );
 CREATE INDEX IX_AdminAuditEvents_CreatedAtUtc ON dbo.AdminAuditEvents(CreatedAtUtc DESC);
 CREATE INDEX IX_AdminAuditEvents_Entity ON dbo.AdminAuditEvents(EntityType,EntityKey,CreatedAtUtc DESC);
END
GO

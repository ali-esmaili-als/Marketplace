/*
    Transactional Outbox upgrade for existing Marketplace databases.
    Safe to run repeatedly. Fresh databases created from Marketplace_Complete.sql already
    contain this table and index.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.OutboxMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxMessages(
        Id BIGINT NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY,
        MessageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_OutboxMessages_MessageId DEFAULT(NEWID()),
        EventType NVARCHAR(200) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        OccurredAtUtc DATETIME2(7) NOT NULL,
        ProcessedAtUtc DATETIME2(7) NULL,
        LockedUntilUtc DATETIME2(7) NULL,
        NextAttemptAtUtc DATETIME2(7) NOT NULL,
        Attempts INT NOT NULL CONSTRAINT DF_OutboxMessages_Attempts DEFAULT(0),
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_OutboxMessages_Status DEFAULT(N'Pending'),
        LastError NVARCHAR(2000) NULL,
        CONSTRAINT UQ_OutboxMessages_MessageId UNIQUE(MessageId),
        CONSTRAINT CK_OutboxMessages_Status CHECK(Status IN (N'Pending',N'Processing',N'Processed',N'DeadLetter')),
        CONSTRAINT CK_OutboxMessages_Attempts CHECK(Attempts>=0)
    );
END;

IF COL_LENGTH(N'dbo.OutboxMessages', N'LockToken') IS NULL
    ALTER TABLE dbo.OutboxMessages ADD LockToken UNIQUEIDENTIFIER NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboxMessages') AND name=N'IX_OutboxMessages_Poll')
    CREATE INDEX IX_OutboxMessages_Poll ON dbo.OutboxMessages(Status,NextAttemptAtUtc,Id)
        INCLUDE(EventType,MessageId,Attempts);

COMMIT TRANSACTION;

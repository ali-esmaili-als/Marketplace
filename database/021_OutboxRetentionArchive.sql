/*
    Outbox processed-message archive schema.
    Safe to apply repeatedly; does not alter existing Outbox rows or financial records.
    Enable the retention worker only after this migration/schema is deployed.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.OutboxMessageArchive', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxMessageArchive
    (
        Id BIGINT NOT NULL CONSTRAINT PK_OutboxMessageArchive PRIMARY KEY,
        MessageId UNIQUEIDENTIFIER NOT NULL,
        EventType NVARCHAR(200) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        OccurredAtUtc DATETIME2(7) NOT NULL,
        ProcessedAtUtc DATETIME2(7) NOT NULL,
        LockedUntilUtc DATETIME2(7) NULL,
        LockToken UNIQUEIDENTIFIER NULL,
        NextAttemptAtUtc DATETIME2(7) NOT NULL,
        Attempts INT NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        LastError NVARCHAR(2000) NULL,
        ArchivedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_OutboxMessageArchive_ArchivedAtUtc DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT UQ_OutboxMessageArchive_MessageId UNIQUE(MessageId),
        CONSTRAINT CK_OutboxMessageArchive_ProcessedOnly CHECK(Status = N'Processed' AND ProcessedAtUtc IS NOT NULL),
        CONSTRAINT CK_OutboxMessageArchive_Attempts CHECK(Attempts >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutboxMessageArchive') AND name=N'IX_OutboxMessageArchive_ArchivedAtUtc')
    CREATE INDEX IX_OutboxMessageArchive_ArchivedAtUtc ON dbo.OutboxMessageArchive(ArchivedAtUtc, Id);

COMMIT TRANSACTION;

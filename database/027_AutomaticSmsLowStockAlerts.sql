SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.InventoryItems', N'LowStockAlertSent') IS NULL
BEGIN
    ALTER TABLE dbo.InventoryItems
        ADD LowStockAlertSent BIT NOT NULL
            CONSTRAINT DF_InventoryItems_LowStockAlertSent DEFAULT (0) WITH VALUES;
END;

IF OBJECT_ID(N'dbo.SmsAutomationSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SmsAutomationSettings
    (
        Id BIGINT NOT NULL CONSTRAINT PK_SmsAutomationSettings PRIMARY KEY,
        AutomaticSmsEnabled BIT NOT NULL CONSTRAINT DF_SmsAutomationSettings_AutomaticSms DEFAULT (0),
        LowStockSmsEnabled BIT NOT NULL CONSTRAINT DF_SmsAutomationSettings_LowStock DEFAULT (0),
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT CK_SmsAutomationSettings_LowStock CHECK (LowStockSmsEnabled = 0 OR AutomaticSmsEnabled = 1),
        CONSTRAINT CK_SmsAutomationSettings_Singleton CHECK (Id = 1)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SmsAutomationSettings WHERE Id = 1)
    INSERT dbo.SmsAutomationSettings(Id, AutomaticSmsEnabled, LowStockSmsEnabled, UpdatedAtUtc)
    VALUES (1, 0, 0, SYSUTCDATETIME());

COMMIT TRANSACTION;

IF OBJECT_ID(N'dbo.SmsProviderSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SmsProviderSettings
    (
        Id BIGINT NOT NULL CONSTRAINT PK_SmsProviderSettings PRIMARY KEY,
        Provider NVARCHAR(50) NOT NULL,
        DisplayName NVARCHAR(150) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_SmsProviderSettings_IsEnabled DEFAULT(0),
        IsVisible BIT NOT NULL CONSTRAINT DF_SmsProviderSettings_IsVisible DEFAULT(1),
        SortOrder INT NOT NULL CONSTRAINT DF_SmsProviderSettings_SortOrder DEFAULT(0),
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_SmsProviderSettings_Provider UNIQUE(Provider)
    );

    CREATE INDEX IX_SmsProviderSettings_Enabled_Visible_Sort
        ON dbo.SmsProviderSettings(IsEnabled, IsVisible, SortOrder);
END;
GO

MERGE dbo.SmsProviderSettings AS target
USING (VALUES
    (1,N'Test',N'سامانه پیامکی تست',0,1,1),
    (2,N'Kavenegar',N'کاوه نگار',0,1,2),
    (3,N'Melipayamak',N'ملی پیامک',0,1,3),
    (4,N'SmsIr',N'SMS.ir',0,1,4),
    (5,N'HttpApi',N'سرویس پیامکی HTTP API',0,1,5)
) AS source(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder)
ON target.Provider=source.Provider
WHEN MATCHED THEN UPDATE SET DisplayName=source.DisplayName,IsVisible=source.IsVisible,SortOrder=source.SortOrder
WHEN NOT MATCHED THEN
    INSERT(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder,UpdatedAtUtc)
    VALUES(source.Id,source.Provider,source.DisplayName,source.IsEnabled,source.IsVisible,source.SortOrder,SYSUTCDATETIME());
GO

MERGE dbo.Rules AS target
USING (VALUES
    (3006,N'Admin.SmsProviders.Read',N'View SMS provider settings',3),
    (3007,N'Admin.SmsProviders.Configure',N'Configure SMS providers',3)
) AS source(Id,Code,Name,ActionType)
ON target.Id=source.Id
WHEN MATCHED THEN UPDATE SET Code=source.Code,Name=source.Name,ActionType=source.ActionType,IsActive=1
WHEN NOT MATCHED THEN INSERT(Id,Code,Name,ActionType,IsActive) VALUES(source.Id,source.Code,source.Name,source.ActionType,1);
GO
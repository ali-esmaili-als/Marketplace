IF OBJECT_ID(N'dbo.PaymentProviderSettings',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentProviderSettings
    (
        Id BIGINT NOT NULL CONSTRAINT PK_PaymentProviderSettings PRIMARY KEY,
        Provider TINYINT NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_PaymentProviderSettings_IsEnabled DEFAULT(0),
        IsVisible BIT NOT NULL CONSTRAINT DF_PaymentProviderSettings_IsVisible DEFAULT(0),
        SortOrder INT NOT NULL CONSTRAINT DF_PaymentProviderSettings_SortOrder DEFAULT(0),
        ConfigurationJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PaymentProviderSettings_ConfigurationJson DEFAULT(N'{}'),
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_PaymentProviderSettings_Provider UNIQUE(Provider)
    );
END
GO
MERGE dbo.PaymentProviderSettings AS t
USING (VALUES
 (1000000101,1,N'بانک ملی',0,0,1),
 (1000000102,2,N'بانک پارسیان',0,0,2),
 (1000000103,3,N'بانک پاسارگاد',0,0,3),
 (1000000104,4,N'بانک ملت',0,0,4),
 (1000000105,5,N'بانک سپه',0,0,5),
 (1000000106,6,N'بانک تجارت',0,0,6),
 (1000000107,7,N'بانک سامان',0,0,7),
 (1000000108,8,N'بانک تستی (بدون اتصال به بانک واقعی)',1,1,0)
) AS s(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder)
ON t.Provider=s.Provider
WHEN NOT MATCHED THEN
 INSERT(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder,ConfigurationJson,UpdatedAtUtc)
 VALUES(s.Id,s.Provider,s.DisplayName,s.IsEnabled,s.IsVisible,s.SortOrder,N'{}',SYSUTCDATETIME());
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.CustomerAddresses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerAddresses
    (
        Id BIGINT NOT NULL CONSTRAINT PK_CustomerAddresses PRIMARY KEY,
        CustomerId BIGINT NOT NULL,
        CityId BIGINT NOT NULL,
        RecipientName NVARCHAR(150) NOT NULL,
        RecipientMobile NVARCHAR(30) NOT NULL,
        AddressLine NVARCHAR(1000) NOT NULL,
        PostalCode NVARCHAR(20) NOT NULL,
        DeliveryNote NVARCHAR(500) NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_CustomerAddresses_IsDefault DEFAULT(0),
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_CustomerAddresses_Users FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_CustomerAddresses_DeliveryCities FOREIGN KEY(CityId) REFERENCES dbo.DeliveryCities(Id),
        CONSTRAINT CK_CustomerAddresses_Ids CHECK(Id > 0 AND CustomerId > 0 AND CityId > 0),
        CONSTRAINT CK_CustomerAddresses_Required CHECK(LEN(LTRIM(RTRIM(RecipientName))) >= 2 AND LEN(LTRIM(RTRIM(RecipientMobile))) >= 8 AND LEN(LTRIM(RTRIM(AddressLine))) >= 5 AND LEN(LTRIM(RTRIM(PostalCode))) >= 5)
    );
    CREATE INDEX IX_CustomerAddresses_CustomerId ON dbo.CustomerAddresses(CustomerId);
    CREATE UNIQUE INDEX UX_CustomerAddresses_DefaultPerCustomer ON dbo.CustomerAddresses(CustomerId) WHERE IsDefault = 1;
END;
IF COL_LENGTH(N'dbo.Orders', N'DeliveryRecipientNameSnapshot') IS NULL
    ALTER TABLE dbo.Orders ADD DeliveryRecipientNameSnapshot NVARCHAR(150) NULL;
IF COL_LENGTH(N'dbo.Orders', N'DeliveryRecipientMobileSnapshot') IS NULL
    ALTER TABLE dbo.Orders ADD DeliveryRecipientMobileSnapshot NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.Orders', N'DeliveryAddressLineSnapshot') IS NULL
    ALTER TABLE dbo.Orders ADD DeliveryAddressLineSnapshot NVARCHAR(1000) NULL;
IF COL_LENGTH(N'dbo.Orders', N'DeliveryPostalCodeSnapshot') IS NULL
    ALTER TABLE dbo.Orders ADD DeliveryPostalCodeSnapshot NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.Orders', N'DeliveryNoteSnapshot') IS NULL
    ALTER TABLE dbo.Orders ADD DeliveryNoteSnapshot NVARCHAR(500) NULL;
COMMIT TRANSACTION;

IF OBJECT_ID(N'dbo.DeliveryCities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryCities
    (
        Id BIGINT NOT NULL CONSTRAINT PK_DeliveryCities PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        ProvinceName NVARCHAR(200) NOT NULL,
        Code VARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_DeliveryCities_IsActive DEFAULT (1),
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_DeliveryCities_Code UNIQUE (Code)
    );
END;
GO

IF OBJECT_ID(N'dbo.StoreShippingCities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoreShippingCities
    (
        Id BIGINT NOT NULL CONSTRAINT PK_StoreShippingCities PRIMARY KEY,
        StoreId BIGINT NOT NULL,
        CityId BIGINT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_StoreShippingCities_Stores FOREIGN KEY (StoreId) REFERENCES dbo.Stores(Id),
        CONSTRAINT FK_StoreShippingCities_DeliveryCities FOREIGN KEY (CityId) REFERENCES dbo.DeliveryCities(Id),
        CONSTRAINT UQ_StoreShippingCities_Store_City UNIQUE (StoreId, CityId)
    );

    CREATE INDEX IX_StoreShippingCities_StoreId
        ON dbo.StoreShippingCities(StoreId);

    CREATE INDEX IX_StoreShippingCities_CityId
        ON dbo.StoreShippingCities(CityId);
END;
GO

-- Example seed rows can be inserted by the application/admin import.
-- City data is intentionally not hard-coded here because the platform should own
-- the authoritative city master data and may later import/update it.

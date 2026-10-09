/*
 024_StoreShippingRates.sql
 Adds per-store/per-destination shipping prices and delivery estimates.
 Existing shipping coverage is backfilled with a zero fee and a conservative 3-7 day estimate
 so current stores remain checkout-capable; sellers can edit these values afterward.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Orders', N'ShippingFeeIRR') IS NULL
BEGIN
    ALTER TABLE dbo.Orders ADD ShippingFeeIRR BIGINT NOT NULL
        CONSTRAINT DF_Orders_ShippingFee DEFAULT(0) WITH VALUES;
END;

IF OBJECT_ID(N'dbo.StoreShippingRates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoreShippingRates(
        Id BIGINT NOT NULL CONSTRAINT PK_StoreShippingRates PRIMARY KEY,
        StoreId BIGINT NOT NULL,
        CityId BIGINT NOT NULL,
        ShippingFeeIRR BIGINT NOT NULL,
        MinDeliveryDays INT NOT NULL,
        MaxDeliveryDays INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_StoreShippingRates_Store_City UNIQUE(StoreId,CityId),
        CONSTRAINT FK_StoreShippingRates_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id) ON DELETE CASCADE,
        CONSTRAINT FK_StoreShippingRates_DeliveryCities FOREIGN KEY(CityId) REFERENCES dbo.DeliveryCities(Id),
        CONSTRAINT CK_StoreShippingRates_Values CHECK(ShippingFeeIRR>=0 AND MinDeliveryDays>=0 AND MaxDeliveryDays>=MinDeliveryDays AND MaxDeliveryDays<=365)
    );
    CREATE INDEX IX_StoreShippingRates_CityId ON dbo.StoreShippingRates(CityId);
END;

IF OBJECT_ID(N'dbo.MarketplaceSequence', N'SQ') IS NULL
    THROW 51000, 'dbo.MarketplaceSequence is required before applying shipping-rate migration.', 1;

INSERT INTO dbo.StoreShippingRates(Id,StoreId,CityId,ShippingFeeIRR,MinDeliveryDays,MaxDeliveryDays,CreatedAtUtc,UpdatedAtUtc)
SELECT NEXT VALUE FOR dbo.MarketplaceSequence, coverage.StoreId, coverage.CityId, 0, 3, 7, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM dbo.StoreShippingCities AS coverage
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.StoreShippingRates AS rate
    WHERE rate.StoreId = coverage.StoreId AND rate.CityId = coverage.CityId
);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Orders') AND name = N'CK_Orders_Amounts')
    ALTER TABLE dbo.Orders DROP CONSTRAINT CK_Orders_Amounts;

ALTER TABLE dbo.Orders WITH CHECK ADD CONSTRAINT CK_Orders_Amounts
    CHECK(SubtotalAmountIRR>0 AND ShippingFeeIRR>=0 AND TotalAmountIRR>0
      AND TotalAmountIRR<=SubtotalAmountIRR+ShippingFeeIRR
      AND SellerAmountIRR>=0 AND SellerAmountIRR<=TotalAmountIRR);

COMMIT TRANSACTION;
GO

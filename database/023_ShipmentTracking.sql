/*
 023_ShipmentTracking.sql
 Incremental upgrade for existing Marketplace databases.
 Adds carrier tracking only; carrier delivery status never confirms the platform's delivery code
 and never releases seller funds by itself.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Shipments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Shipments(
        Id BIGINT NOT NULL CONSTRAINT PK_Shipments PRIMARY KEY,
        OrderId BIGINT NOT NULL,
        SellerId BIGINT NOT NULL,
        CarrierName NVARCHAR(150) NOT NULL,
        TrackingNumber NVARCHAR(150) NOT NULL,
        TrackingUrl NVARCHAR(1000) NULL,
        Status TINYINT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        ShippedAtUtc DATETIME2(7) NULL,
        CarrierDeliveredAtUtc DATETIME2(7) NULL,
        CONSTRAINT UQ_Shipments_Order UNIQUE(OrderId),
        CONSTRAINT FK_Shipments_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
        CONSTRAINT FK_Shipments_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
        CONSTRAINT CK_Shipments_Status CHECK(Status BETWEEN 1 AND 8),
        CONSTRAINT CK_Shipments_Tracking CHECK(LEN(LTRIM(RTRIM(CarrierName)))>0 AND LEN(LTRIM(RTRIM(TrackingNumber)))>0)
    );
    CREATE INDEX IX_Shipments_Seller_Status_Updated ON dbo.Shipments(SellerId,Status,UpdatedAtUtc);
END;

IF OBJECT_ID(N'dbo.ShipmentTrackingEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShipmentTrackingEvents(
        Id BIGINT NOT NULL CONSTRAINT PK_ShipmentTrackingEvents PRIMARY KEY,
        ShipmentId BIGINT NOT NULL,
        Status TINYINT NOT NULL,
        Description NVARCHAR(1000) NOT NULL,
        Location NVARCHAR(200) NULL,
        ActorUserId BIGINT NOT NULL,
        OccurredAtUtc DATETIME2(7) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_ShipmentTrackingEvents_Shipments FOREIGN KEY(ShipmentId) REFERENCES dbo.Shipments(Id),
        CONSTRAINT FK_ShipmentTrackingEvents_Users FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ShipmentTrackingEvents_Status CHECK(Status BETWEEN 1 AND 8),
        CONSTRAINT CK_ShipmentTrackingEvents_Description CHECK(LEN(LTRIM(RTRIM(Description)))>0)
    );
    CREATE INDEX IX_ShipmentTrackingEvents_Shipment_Occurred ON dbo.ShipmentTrackingEvents(ShipmentId,OccurredAtUtc,Id);
END;

COMMIT TRANSACTION;
GO

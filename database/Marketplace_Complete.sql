/*
 Marketplace - SINGLE CANONICAL SQL SERVER SCHEMA
 Branch: develop

 This is the source of truth for a NEW / EMPTY database. It consolidates the current schema
 changes previously introduced by the numbered database upgrades, including identity and
 permission tables, seller/catalog, campaigns/coupons, shipping coverage and order destination
 snapshots, payment/refund/settlement lifecycle constraints and idempotency indexes, balance
 buckets, reconciliation/audit tables, SMS/payment provider settings, and the transactional
 outbox (including LockToken, polling index, and retry/audit support).

 Run this file ONCE against an empty Marketplace database. Do NOT run the numbered patch files
 afterward on a fresh database. Numbered patch files remain only as incremental upgrade history
 for databases that already existed before this consolidated schema; they are not required for
 fresh installs. This script validates the presence of all required tables at the end.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MarketplaceSequence', N'SQ') IS NULL
    EXEC(N'CREATE SEQUENCE dbo.MarketplaceSequence AS BIGINT START WITH 1000000000 INCREMENT BY 1;');

CREATE TABLE dbo.Users(
 Id BIGINT NOT NULL CONSTRAINT PK_Users PRIMARY KEY, Mobile NVARCHAR(30) NOT NULL, Email NVARCHAR(320) NULL,
 PasswordHash NVARCHAR(500) NOT NULL, DisplayName NVARCHAR(200) NOT NULL,
 IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
 IsMobileVerified BIT NOT NULL CONSTRAINT DF_Users_IsMobileVerified DEFAULT(0),
 CreatedAtUtc DATETIME2(7) NOT NULL, LastLoginAtUtc DATETIME2(7) NULL,
 CONSTRAINT UQ_Users_Mobile UNIQUE(Mobile)
);
CREATE UNIQUE INDEX UX_Users_Email_NotNull ON dbo.Users(Email) WHERE Email IS NOT NULL;

CREATE TABLE dbo.Roles(
 Id BIGINT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY, Name NVARCHAR(50) NOT NULL,
 IsActive BIT NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT(1), CONSTRAINT UQ_Roles_Name UNIQUE(Name)
);
CREATE TABLE dbo.Rules(
 Id BIGINT NOT NULL CONSTRAINT PK_Rules PRIMARY KEY, Code NVARCHAR(150) NOT NULL, Name NVARCHAR(250) NOT NULL,
 ActionType TINYINT NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_Rules_IsActive DEFAULT(1),
 CONSTRAINT UQ_Rules_Code UNIQUE(Code)
);
CREATE TABLE dbo.UserRules(
 Id BIGINT NOT NULL CONSTRAINT PK_UserRules PRIMARY KEY, UserId BIGINT NOT NULL, RuleId BIGINT NOT NULL,
 GrantedAtUtc DATETIME2(7) NOT NULL, CONSTRAINT UQ_UserRules_User_Rule UNIQUE(UserId,RuleId)
);
CREATE TABLE dbo.UserRoleAssignments(
 Id BIGINT NOT NULL CONSTRAINT PK_UserRoleAssignments PRIMARY KEY, UserId BIGINT NOT NULL, RoleId BIGINT NOT NULL,
 CONSTRAINT UQ_UserRoleAssignments_User_Role UNIQUE(UserId,RoleId)
);

CREATE TABLE dbo.Sellers(
 Id BIGINT NOT NULL CONSTRAINT PK_Sellers PRIMARY KEY, UserId BIGINT NOT NULL, Status TINYINT NOT NULL,
 CommissionRateBasisPoints INT NOT NULL CONSTRAINT DF_Sellers_CommissionRate DEFAULT(0),
 MinimumCommissionIRR BIGINT NOT NULL CONSTRAINT DF_Sellers_MinCommission DEFAULT(0),
 MaxStoreCount INT NOT NULL CONSTRAINT DF_Sellers_MaxStoreCount DEFAULT(1),
 CreatedAtUtc DATETIME2(7) NOT NULL, ActivatedAtUtc DATETIME2(7) NULL,
 CONSTRAINT UQ_Sellers_User UNIQUE(UserId),
 CONSTRAINT CK_Sellers_Commission CHECK(CommissionRateBasisPoints BETWEEN 0 AND 10000 AND MinimumCommissionIRR>=0),
 CONSTRAINT CK_Sellers_MaxStore CHECK(MaxStoreCount>0)
);
CREATE TABLE dbo.Stores(
 Id BIGINT NOT NULL CONSTRAINT PK_Stores PRIMARY KEY, SellerId BIGINT NOT NULL,
 Name NVARCHAR(200) NOT NULL, Slug NVARCHAR(250) NOT NULL, Description NVARCHAR(2000) NULL,
 Status TINYINT NOT NULL, CommissionRateBasisPoints INT NOT NULL CONSTRAINT DF_Stores_CommissionRate DEFAULT(0),
 MinimumCommissionIRR BIGINT NOT NULL CONSTRAINT DF_Stores_MinCommission DEFAULT(0),
 CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_Stores_Seller_Slug UNIQUE(SellerId,Slug),
 CONSTRAINT CK_Stores_Commission CHECK(CommissionRateBasisPoints BETWEEN 0 AND 10000 AND MinimumCommissionIRR>=0)
);
CREATE TABLE dbo.SellerBankAccounts(
 Id BIGINT NOT NULL CONSTRAINT PK_SellerBankAccounts PRIMARY KEY, SellerId BIGINT NOT NULL,
 BankName NVARCHAR(150) NOT NULL, Iban NVARCHAR(34) NOT NULL, AccountHolderName NVARCHAR(250) NOT NULL,
 IsDefault BIT NOT NULL CONSTRAINT DF_SellerBankAccounts_IsDefault DEFAULT(0),
 IsVerified BIT NOT NULL CONSTRAINT DF_SellerBankAccounts_IsVerified DEFAULT(0), CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_SellerBankAccounts_Seller_Iban UNIQUE(SellerId,Iban)
);

CREATE TABLE dbo.Categories(
 Id BIGINT NOT NULL CONSTRAINT PK_Categories PRIMARY KEY, ParentCategoryId BIGINT NULL,
 Name NVARCHAR(200) NOT NULL, Slug NVARCHAR(250) NOT NULL, Path VARCHAR(850) NOT NULL,
 IsActive BIT NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT(1), CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_Categories_Parent_Slug UNIQUE(ParentCategoryId,Slug)
);
CREATE TABLE dbo.Products(
 Id BIGINT NOT NULL CONSTRAINT PK_Products PRIMARY KEY, StoreId BIGINT NOT NULL, CategoryId BIGINT NOT NULL,
 Name NVARCHAR(300) NOT NULL, Slug NVARCHAR(350) NOT NULL, Description NVARCHAR(MAX) NULL,
 BasePriceIRR BIGINT NOT NULL, Status TINYINT NOT NULL, HasVariants BIT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_Products_Store_Slug UNIQUE(StoreId,Slug), CONSTRAINT CK_Products_Price CHECK(BasePriceIRR>=0)
);
CREATE TABLE dbo.ProductVariants(
 Id BIGINT NOT NULL CONSTRAINT PK_ProductVariants PRIMARY KEY, ProductId BIGINT NOT NULL,
 SKU NVARCHAR(150) NOT NULL, VariantKey NVARCHAR(1000) NOT NULL, VariantKeyHash BINARY(32) NOT NULL,
 PriceIRR BIGINT NULL, IsActive BIT NOT NULL CONSTRAINT DF_ProductVariants_IsActive DEFAULT(1), CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_ProductVariants_Product_SKU UNIQUE(ProductId,SKU),
 CONSTRAINT UQ_ProductVariants_Product_Hash UNIQUE(ProductId,VariantKeyHash),
 CONSTRAINT CK_ProductVariants_Price CHECK(PriceIRR IS NULL OR PriceIRR>=0)
);
CREATE TABLE dbo.ProductAttributes(
 Id BIGINT NOT NULL CONSTRAINT PK_ProductAttributes PRIMARY KEY, StoreId BIGINT NOT NULL,
 Name NVARCHAR(150) NOT NULL, Slug NVARCHAR(200) NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_ProductAttributes_IsActive DEFAULT(1),
 CONSTRAINT UQ_ProductAttributes_Store_Slug UNIQUE(StoreId,Slug)
);
CREATE TABLE dbo.ProductAttributeValues(
 Id BIGINT NOT NULL CONSTRAINT PK_ProductAttributeValues PRIMARY KEY, ProductAttributeId BIGINT NOT NULL,
 Value NVARCHAR(150) NOT NULL, Slug NVARCHAR(200) NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_ProductAttributeValues_IsActive DEFAULT(1),
 CONSTRAINT UQ_ProductAttributeValues_Attribute_Slug UNIQUE(ProductAttributeId,Slug)
);
CREATE TABLE dbo.ProductAttributeAssignments(
 Id BIGINT NOT NULL CONSTRAINT PK_ProductAttributeAssignments PRIMARY KEY, ProductId BIGINT NOT NULL, ProductAttributeId BIGINT NOT NULL,
 CONSTRAINT UQ_ProductAttributeAssignments_Product_Attribute UNIQUE(ProductId,ProductAttributeId)
);
CREATE TABLE dbo.VariantAttributeValues(
 Id BIGINT NOT NULL CONSTRAINT PK_VariantAttributeValues PRIMARY KEY, ProductVariantId BIGINT NOT NULL, ProductAttributeValueId BIGINT NOT NULL,
 CONSTRAINT UQ_VariantAttributeValues_Variant_Value UNIQUE(ProductVariantId,ProductAttributeValueId)
);
CREATE TABLE dbo.Warranties(
 Id BIGINT NOT NULL CONSTRAINT PK_Warranties PRIMARY KEY, StoreId BIGINT NOT NULL, Name NVARCHAR(250) NOT NULL,
 PriceIRR BIGINT NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_Warranties_IsActive DEFAULT(1),
 CONSTRAINT UQ_Warranties_Store_Name UNIQUE(StoreId,Name), CONSTRAINT CK_Warranties_Price CHECK(PriceIRR>=0)
);
CREATE TABLE dbo.ProductWarranties(
 Id BIGINT NOT NULL CONSTRAINT PK_ProductWarranties PRIMARY KEY, ProductId BIGINT NOT NULL, WarrantyId BIGINT NOT NULL,
 IsDefault BIT NOT NULL CONSTRAINT DF_ProductWarranties_IsDefault DEFAULT(0),
 IsActive BIT NOT NULL CONSTRAINT DF_ProductWarranties_IsActive DEFAULT(1),
 CONSTRAINT UQ_ProductWarranties_Product_Warranty UNIQUE(ProductId,WarrantyId)
);

CREATE TABLE dbo.Carts(
 Id BIGINT NOT NULL CONSTRAINT PK_Carts PRIMARY KEY, CustomerId BIGINT NOT NULL, StoreId BIGINT NOT NULL, SellerId BIGINT NOT NULL,
 CreatedAtUtc DATETIME2(7) NOT NULL, UpdatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_Carts_Customer UNIQUE(CustomerId)
);
CREATE TABLE dbo.CartItems(
 Id BIGINT NOT NULL CONSTRAINT PK_CartItems PRIMARY KEY, CartId BIGINT NOT NULL, ProductId BIGINT NOT NULL,
 ProductVariantId BIGINT NOT NULL, WarrantyId BIGINT NULL, Quantity INT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_CartItems_Cart_Variant_Warranty UNIQUE(CartId,ProductVariantId,WarrantyId), CONSTRAINT CK_CartItems_Quantity CHECK(Quantity>0)
);

CREATE TABLE dbo.DeliveryCities(
 Id BIGINT NOT NULL CONSTRAINT PK_DeliveryCities PRIMARY KEY, Name NVARCHAR(200) NOT NULL, ProvinceName NVARCHAR(200) NOT NULL,
 Code VARCHAR(50) NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_DeliveryCities_IsActive DEFAULT(1), CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_DeliveryCities_Code UNIQUE(Code)
);
CREATE TABLE dbo.StoreShippingCities(
 Id BIGINT NOT NULL CONSTRAINT PK_StoreShippingCities PRIMARY KEY, StoreId BIGINT NOT NULL, CityId BIGINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_StoreShippingCities_Store_City UNIQUE(StoreId,CityId)
);
CREATE TABLE dbo.StoreShippingRates(
 Id BIGINT NOT NULL CONSTRAINT PK_StoreShippingRates PRIMARY KEY, StoreId BIGINT NOT NULL, CityId BIGINT NOT NULL,
 ShippingFeeIRR BIGINT NOT NULL, MinDeliveryDays INT NOT NULL, MaxDeliveryDays INT NOT NULL,
 CreatedAtUtc DATETIME2(7) NOT NULL, UpdatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_StoreShippingRates_Store_City UNIQUE(StoreId,CityId),
 CONSTRAINT CK_StoreShippingRates_Values CHECK(ShippingFeeIRR>=0 AND MinDeliveryDays>=0 AND MaxDeliveryDays>=MinDeliveryDays AND MaxDeliveryDays<=365)
);

CREATE TABLE dbo.Orders(
 Id BIGINT NOT NULL CONSTRAINT PK_Orders PRIMARY KEY, CustomerId BIGINT NOT NULL, RequestKey NVARCHAR(64) NULL, SellerId BIGINT NOT NULL, StoreId BIGINT NOT NULL,
 SubtotalAmountIRR BIGINT NOT NULL, ShippingFeeIRR BIGINT NOT NULL CONSTRAINT DF_Orders_ShippingFee DEFAULT(0), CampaignDiscountIRR BIGINT NOT NULL CONSTRAINT DF_Orders_CampaignDiscount DEFAULT(0),
 CouponDiscountIRR BIGINT NOT NULL CONSTRAINT DF_Orders_CouponDiscount DEFAULT(0), CouponCodeSnapshot NVARCHAR(100) NULL,
 TotalAmountIRR BIGINT NOT NULL, SellerAmountIRR BIGINT NOT NULL, DestinationCityId BIGINT NULL,
 DestinationCityNameSnapshot NVARCHAR(200) NULL, DestinationProvinceNameSnapshot NVARCHAR(200) NULL,
 Status TINYINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL, PaidAtUtc DATETIME2(7) NULL,
 DeliveredAtUtc DATETIME2(7) NULL, DeliveryExpiresAtUtc DATETIME2(7) NULL, ComplaintExpiresAtUtc DATETIME2(7) NULL,
 CONSTRAINT CK_Orders_Amounts CHECK(SubtotalAmountIRR>0 AND ShippingFeeIRR>=0 AND TotalAmountIRR>0 AND TotalAmountIRR<=SubtotalAmountIRR+ShippingFeeIRR AND SellerAmountIRR>=0 AND SellerAmountIRR<=TotalAmountIRR),
 CONSTRAINT CK_Orders_Status CHECK(Status BETWEEN 1 AND 10)
);
CREATE UNIQUE INDEX UX_Orders_Customer_RequestKey ON dbo.Orders(CustomerId,RequestKey) WHERE RequestKey IS NOT NULL;
CREATE TABLE dbo.OrderItems(
 Id BIGINT NOT NULL CONSTRAINT PK_OrderItems PRIMARY KEY, OrderId BIGINT NOT NULL, ProductId BIGINT NOT NULL, VariantId BIGINT NULL,
 WarrantyId BIGINT NOT NULL, ProductNameSnapshot NVARCHAR(300) NOT NULL, VariantSnapshot NVARCHAR(1000) NULL, WarrantySnapshot NVARCHAR(500) NULL,
 BaseUnitPriceIRR BIGINT NOT NULL, UnitPriceIRR BIGINT NOT NULL, WarrantyPriceIRR BIGINT NOT NULL,
 CampaignDiscountIRR BIGINT NOT NULL CONSTRAINT DF_OrderItems_CampaignDiscount DEFAULT(0),
 CouponDiscountIRR BIGINT NOT NULL CONSTRAINT DF_OrderItems_CouponDiscount DEFAULT(0),
 CampaignId BIGINT NULL, CampaignNameSnapshot NVARCHAR(250) NULL, Quantity INT NOT NULL, LineTotalIRR BIGINT NOT NULL,
 CONSTRAINT CK_OrderItems_Amounts CHECK(BaseUnitPriceIRR>=0 AND UnitPriceIRR>=0 AND WarrantyPriceIRR>=0 AND CampaignDiscountIRR>=0 AND CouponDiscountIRR>=0 AND Quantity>0 AND LineTotalIRR>=0)
);

CREATE TABLE dbo.Payments(
 Id BIGINT NOT NULL CONSTRAINT PK_Payments PRIMARY KEY, OrderId BIGINT NOT NULL, CustomerId BIGINT NOT NULL,
 AmountIRR BIGINT NOT NULL, Status TINYINT NOT NULL, Provider NVARCHAR(100) NULL, Authority NVARCHAR(200) NULL, RedirectUrl NVARCHAR(2048) NULL,
 ReferenceNumber NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL, PaidAtUtc DATETIME2(7) NULL, RefundedAtUtc DATETIME2(7) NULL,
 CONSTRAINT UQ_Payments_Order UNIQUE(OrderId), CONSTRAINT CK_Payments_Amount CHECK(AmountIRR>0),
 CONSTRAINT CK_Payments_Status CHECK(Status BETWEEN 1 AND 8)
);
CREATE TABLE dbo.PaymentTransactions(
 Id BIGINT NOT NULL CONSTRAINT PK_PaymentTransactions PRIMARY KEY, PaymentId BIGINT NOT NULL, AmountIRR BIGINT NOT NULL,
 Status TINYINT NOT NULL, Provider NVARCHAR(100) NOT NULL, Authority NVARCHAR(200) NULL, Reference NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT CK_PaymentTransactions_Amount CHECK(AmountIRR>0),
 CONSTRAINT CK_PaymentTransactions_Status CHECK(Status BETWEEN 1 AND 4)
);
CREATE TABLE dbo.PaymentProviderSettings(
 Id BIGINT NOT NULL CONSTRAINT PK_PaymentProviderSettings PRIMARY KEY, Provider TINYINT NOT NULL, DisplayName NVARCHAR(100) NOT NULL,
 IsEnabled BIT NOT NULL CONSTRAINT DF_PaymentProviderSettings_IsEnabled DEFAULT(0),
 IsVisible BIT NOT NULL CONSTRAINT DF_PaymentProviderSettings_IsVisible DEFAULT(0),
 SortOrder INT NOT NULL CONSTRAINT DF_PaymentProviderSettings_SortOrder DEFAULT(0),
 ConfigurationJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PaymentProviderSettings_ConfigurationJson DEFAULT(N'{}'),
 UpdatedAtUtc DATETIME2(7) NOT NULL, CONSTRAINT UQ_PaymentProviderSettings_Provider UNIQUE(Provider)
);

CREATE TABLE dbo.PaymentReconciliationAudits(
 Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentReconciliationAudits PRIMARY KEY,
 PaymentId BIGINT NOT NULL, AdminUserId BIGINT NOT NULL, Action NVARCHAR(30) NOT NULL,
 Note NVARCHAR(2000) NOT NULL, BankReference NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT FK_PaymentReconciliationAudits_Payments FOREIGN KEY(PaymentId) REFERENCES dbo.Payments(Id),
 CONSTRAINT CK_PaymentReconciliationAudits_Action CHECK(Action IN (N'RefundCompleted',N'KeepOpen')),
 CONSTRAINT CK_PaymentReconciliationAudits_Note CHECK(LEN(LTRIM(RTRIM(Note)))>0),
 CONSTRAINT CK_PaymentReconciliationAudits_BankReference CHECK(Action<>N'RefundCompleted' OR LEN(LTRIM(RTRIM(ISNULL(BankReference,N''))))>0)
);


CREATE TABLE dbo.Deliveries(
 Id BIGINT NOT NULL CONSTRAINT PK_Deliveries PRIMARY KEY, OrderId BIGINT NOT NULL, SellerId BIGINT NOT NULL, Status TINYINT NOT NULL,
 ReadyAtUtc DATETIME2(7) NULL, DeliveredAtUtc DATETIME2(7) NULL, ExpiresAtUtc DATETIME2(7) NOT NULL, ConfirmationReference NVARCHAR(200) NULL,
 CONSTRAINT UQ_Deliveries_Order UNIQUE(OrderId), CONSTRAINT CK_Deliveries_Status CHECK(Status BETWEEN 1 AND 5)
);
CREATE TABLE dbo.Shipments(
 Id BIGINT NOT NULL CONSTRAINT PK_Shipments PRIMARY KEY, OrderId BIGINT NOT NULL, SellerId BIGINT NOT NULL,
 CarrierName NVARCHAR(150) NOT NULL, TrackingNumber NVARCHAR(150) NOT NULL, TrackingUrl NVARCHAR(1000) NULL,
 Status TINYINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL, UpdatedAtUtc DATETIME2(7) NOT NULL,
 ShippedAtUtc DATETIME2(7) NULL, CarrierDeliveredAtUtc DATETIME2(7) NULL,
 CONSTRAINT UQ_Shipments_Order UNIQUE(OrderId),
 CONSTRAINT CK_Shipments_Status CHECK(Status BETWEEN 1 AND 8),
 CONSTRAINT CK_Shipments_Tracking CHECK(LEN(LTRIM(RTRIM(CarrierName)))>0 AND LEN(LTRIM(RTRIM(TrackingNumber)))>0)
);
CREATE TABLE dbo.ShipmentTrackingEvents(
 Id BIGINT NOT NULL CONSTRAINT PK_ShipmentTrackingEvents PRIMARY KEY, ShipmentId BIGINT NOT NULL,
 Status TINYINT NOT NULL, Description NVARCHAR(1000) NOT NULL, Location NVARCHAR(200) NULL,
 ActorUserId BIGINT NOT NULL, OccurredAtUtc DATETIME2(7) NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT CK_ShipmentTrackingEvents_Status CHECK(Status BETWEEN 1 AND 8),
 CONSTRAINT CK_ShipmentTrackingEvents_Description CHECK(LEN(LTRIM(RTRIM(Description)))>0)
);
CREATE TABLE dbo.DeliveryCodes(
 Id BIGINT NOT NULL CONSTRAINT PK_DeliveryCodes PRIMARY KEY, OrderId BIGINT NOT NULL, CodeHash BINARY(32) NOT NULL,
 ExpiresAtUtc DATETIME2(7) NOT NULL, IssuedAtUtc DATETIME2(7) NOT NULL, UsedAtUtc DATETIME2(7) NULL,
 FailedAttempts INT NOT NULL CONSTRAINT DF_DeliveryCodes_FailedAttempts DEFAULT(0),
 CONSTRAINT UQ_DeliveryCodes_Order UNIQUE(OrderId), CONSTRAINT CK_DeliveryCodes_FailedAttempts CHECK(FailedAttempts>=0)
);
CREATE TABLE dbo.Refunds(
 Id BIGINT NOT NULL CONSTRAINT PK_Refunds PRIMARY KEY, OrderId BIGINT NOT NULL, PaymentId BIGINT NOT NULL, CustomerId BIGINT NOT NULL,
 AmountIRR BIGINT NOT NULL, Reason TINYINT NOT NULL, Status TINYINT NOT NULL, ProviderReference NVARCHAR(200) NULL,
 FailureReason NVARCHAR(1000) NULL, RequestedAtUtc DATETIME2(7) NOT NULL, CompletedAtUtc DATETIME2(7) NULL,
 CONSTRAINT CK_Refunds_Amount CHECK(AmountIRR>0),
 CONSTRAINT CK_Refunds_Status CHECK(Status BETWEEN 1 AND 6)
);
CREATE TABLE dbo.RefundReconciliationAudits(
 Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefundReconciliationAudits PRIMARY KEY,
 RefundId BIGINT NOT NULL, AdminUserId BIGINT NOT NULL, TransferCompleted BIT NOT NULL,
 Note NVARCHAR(2000) NOT NULL, BankReference NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT FK_RefundReconciliationAudits_Refunds FOREIGN KEY(RefundId) REFERENCES dbo.Refunds(Id),
 CONSTRAINT CK_RefundReconciliationAudits_Note CHECK(LEN(LTRIM(RTRIM(Note)))>0),
 CONSTRAINT CK_RefundReconciliationAudits_BankReference CHECK(TransferCompleted=0 OR LEN(LTRIM(RTRIM(ISNULL(BankReference,N''))))>0)
);
CREATE TABLE dbo.Complaints(
 Id BIGINT NOT NULL CONSTRAINT PK_Complaints PRIMARY KEY, OrderId BIGINT NOT NULL, CustomerId BIGINT NOT NULL, SellerId BIGINT NOT NULL,
 Status TINYINT NOT NULL, Reason NVARCHAR(2000) NOT NULL, ResolutionNote NVARCHAR(4000) NULL,
 CreatedAtUtc DATETIME2(7) NOT NULL, ResolvedAtUtc DATETIME2(7) NULL,
 CONSTRAINT CK_Complaints_Status CHECK(Status BETWEEN 1 AND 6)
);

CREATE TABLE dbo.InventoryItems(
 Id BIGINT NOT NULL CONSTRAINT PK_InventoryItems PRIMARY KEY, ProductVariantId BIGINT NOT NULL, StockQuantity BIGINT NOT NULL,
 ReservedQuantity BIGINT NOT NULL CONSTRAINT DF_InventoryItems_Reserved DEFAULT(0), IsActive BIT NOT NULL CONSTRAINT DF_InventoryItems_IsActive DEFAULT(1),
 CONSTRAINT UQ_InventoryItems_Variant UNIQUE(ProductVariantId),
 CONSTRAINT CK_InventoryItems_Qty CHECK(StockQuantity>=0 AND ReservedQuantity>=0 AND ReservedQuantity<=StockQuantity)
);
CREATE TABLE dbo.InventoryReservations(
 Id BIGINT NOT NULL CONSTRAINT PK_InventoryReservations PRIMARY KEY, ProductVariantId BIGINT NOT NULL, OrderId BIGINT NOT NULL,
 Quantity BIGINT NOT NULL, Status TINYINT NOT NULL, ExpiresAtUtc DATETIME2(7) NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT CK_InventoryReservations_Quantity CHECK(Quantity>0), CONSTRAINT CK_InventoryReservations_Status CHECK(Status BETWEEN 1 AND 4)
);

CREATE TABLE dbo.SellerBalances(
 Id BIGINT NOT NULL CONSTRAINT PK_SellerBalances PRIMARY KEY, SellerId BIGINT NOT NULL,
 AvailableIRR BIGINT NOT NULL CONSTRAINT DF_SellerBalances_Available DEFAULT(0),
 PendingIRR BIGINT NOT NULL CONSTRAINT DF_SellerBalances_Pending DEFAULT(0),
 BlockedIRR BIGINT NOT NULL CONSTRAINT DF_SellerBalances_Blocked DEFAULT(0),
 ReservedForSettlementIRR BIGINT NOT NULL CONSTRAINT DF_SellerBalances_Reserved DEFAULT(0),
 LiabilityIRR BIGINT NOT NULL CONSTRAINT DF_SellerBalances_Liability DEFAULT(0),
 UpdatedAtUtc DATETIME2(7) NOT NULL, CONSTRAINT UQ_SellerBalances_Seller UNIQUE(SellerId),
 CONSTRAINT CK_SellerBalances_NonNegative CHECK(AvailableIRR>=0 AND PendingIRR>=0 AND BlockedIRR>=0 AND ReservedForSettlementIRR>=0 AND LiabilityIRR>=0)
);
CREATE TABLE dbo.SellerBalanceHolds(
 Id BIGINT NOT NULL CONSTRAINT PK_SellerBalanceHolds PRIMARY KEY, SellerId BIGINT NOT NULL, OrderId BIGINT NULL,
 AmountIRR BIGINT NOT NULL, Reason NVARCHAR(500) NOT NULL, Status TINYINT NOT NULL,
 CreatedAtUtc DATETIME2(7) NOT NULL, CompletedAtUtc DATETIME2(7) NULL, CONSTRAINT CK_SellerBalanceHolds_Amount CHECK(AmountIRR>0), CONSTRAINT CK_SellerBalanceHolds_Status CHECK(Status BETWEEN 1 AND 3)
);
CREATE TABLE dbo.Settlements(
 Id BIGINT NOT NULL CONSTRAINT PK_Settlements PRIMARY KEY, SellerId BIGINT NOT NULL, RequestKey NVARCHAR(64) NULL, AmountIRR BIGINT NOT NULL, Status TINYINT NOT NULL,
 BankAccountId BIGINT NOT NULL, BankNameSnapshot NVARCHAR(150) NOT NULL, IbanSnapshot NVARCHAR(34) NOT NULL,
 AccountHolderNameSnapshot NVARCHAR(250) NOT NULL, Reference NVARCHAR(200) NULL, FailureReason NVARCHAR(1000) NULL,
 RequestedAtUtc DATETIME2(7) NOT NULL, CompletedAtUtc DATETIME2(7) NULL, CONSTRAINT CK_Settlements_Amount CHECK(AmountIRR>0),
 CONSTRAINT CK_Settlements_Status CHECK(Status BETWEEN 1 AND 6)
);
CREATE TABLE dbo.OutboxMessages(
 Id BIGINT NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY,
 MessageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_OutboxMessages_MessageId DEFAULT(NEWID()),
 EventType NVARCHAR(200) NOT NULL, PayloadJson NVARCHAR(MAX) NOT NULL,
 OccurredAtUtc DATETIME2(7) NOT NULL, ProcessedAtUtc DATETIME2(7) NULL,
 LockedUntilUtc DATETIME2(7) NULL, LockToken UNIQUEIDENTIFIER NULL, NextAttemptAtUtc DATETIME2(7) NOT NULL,
 Attempts INT NOT NULL CONSTRAINT DF_OutboxMessages_Attempts DEFAULT(0),
 Status NVARCHAR(20) NOT NULL CONSTRAINT DF_OutboxMessages_Status DEFAULT(N'Pending'),
 LastError NVARCHAR(2000) NULL,
 CONSTRAINT UQ_OutboxMessages_MessageId UNIQUE(MessageId),
 CONSTRAINT CK_OutboxMessages_Status CHECK(Status IN (N'Pending',N'Processing',N'Processed',N'DeadLetter')),
 CONSTRAINT CK_OutboxMessages_Attempts CHECK(Attempts>=0)
);
CREATE INDEX IX_OutboxMessages_Poll ON dbo.OutboxMessages(Status,NextAttemptAtUtc,Id)
 INCLUDE(EventType,MessageId,Attempts);

CREATE TABLE dbo.OutboxMessageArchive
(
 Id BIGINT NOT NULL CONSTRAINT PK_OutboxMessageArchive PRIMARY KEY,
 MessageId UNIQUEIDENTIFIER NOT NULL,
 EventType NVARCHAR(200) NOT NULL, PayloadJson NVARCHAR(MAX) NOT NULL,
 OccurredAtUtc DATETIME2(7) NOT NULL, ProcessedAtUtc DATETIME2(7) NOT NULL,
 LockedUntilUtc DATETIME2(7) NULL, LockToken UNIQUEIDENTIFIER NULL,
 NextAttemptAtUtc DATETIME2(7) NOT NULL, Attempts INT NOT NULL,
 Status NVARCHAR(20) NOT NULL, LastError NVARCHAR(2000) NULL,
 ArchivedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_OutboxMessageArchive_ArchivedAtUtc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT UQ_OutboxMessageArchive_MessageId UNIQUE(MessageId),
 CONSTRAINT CK_OutboxMessageArchive_ProcessedOnly CHECK(Status=N'Processed' AND ProcessedAtUtc IS NOT NULL),
 CONSTRAINT CK_OutboxMessageArchive_Attempts CHECK(Attempts>=0)
);
CREATE INDEX IX_OutboxMessageArchive_ArchivedAtUtc ON dbo.OutboxMessageArchive(ArchivedAtUtc, Id);

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

CREATE TABLE dbo.SettlementReconciliationAudits(
 Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SettlementReconciliationAudits PRIMARY KEY,
 SettlementId BIGINT NOT NULL, AdminUserId BIGINT NOT NULL, TransferCompleted BIT NOT NULL,
 Note NVARCHAR(2000) NOT NULL, BankReference NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT FK_SettlementReconciliationAudits_Settlements FOREIGN KEY(SettlementId) REFERENCES dbo.Settlements(Id),
 CONSTRAINT CK_SettlementReconciliationAudits_Note CHECK(LEN(LTRIM(RTRIM(Note)))>0),
 CONSTRAINT CK_SettlementReconciliationAudits_BankReference CHECK(TransferCompleted=0 OR LEN(LTRIM(RTRIM(ISNULL(BankReference,N''))))>0)
);
CREATE TABLE dbo.BalanceTransactions(
 Id BIGINT NOT NULL CONSTRAINT PK_BalanceTransactions PRIMARY KEY, SellerId BIGINT NOT NULL, OrderId BIGINT NULL,
 SettlementId BIGINT NULL, RefundId BIGINT NULL, Type TINYINT NOT NULL, Bucket TINYINT NOT NULL CONSTRAINT DF_BalanceTransactions_Bucket DEFAULT(1),
 AmountIRR BIGINT NOT NULL, BalanceBeforeIRR BIGINT NOT NULL, BalanceAfterIRR BIGINT NOT NULL, Reference NVARCHAR(200) NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT CK_BalanceTransactions_Amounts CHECK(AmountIRR>=0 AND BalanceBeforeIRR>=0 AND BalanceAfterIRR>=0), CONSTRAINT CK_BalanceTransactions_Type CHECK(Type BETWEEN 1 AND 14), CONSTRAINT CK_BalanceTransactions_Bucket CHECK(Bucket BETWEEN 1 AND 5)
);
CREATE TABLE dbo.Commissions(
 Id BIGINT NOT NULL CONSTRAINT PK_Commissions PRIMARY KEY, OrderId BIGINT NOT NULL, StoreId BIGINT NOT NULL, SellerId BIGINT NOT NULL,
 OrderAmountIRR BIGINT NOT NULL, CommissionRate DECIMAL(9,4) NOT NULL, MinimumCommissionIRR BIGINT NOT NULL,
 CalculatedCommissionIRR BIGINT NOT NULL, CommissionAmountIRR BIGINT NOT NULL, SellerAmountIRR BIGINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_Commissions_Order UNIQUE(OrderId),
 CONSTRAINT CK_Commissions_Amounts CHECK(OrderAmountIRR>=0 AND CommissionRate>=0 AND CommissionRate<=100 AND MinimumCommissionIRR>=0 AND CalculatedCommissionIRR>=0 AND CommissionAmountIRR>=0 AND SellerAmountIRR>=0)
);
CREATE TABLE dbo.CommissionReversals(
 Id BIGINT NOT NULL CONSTRAINT PK_CommissionReversals PRIMARY KEY, CommissionId BIGINT NOT NULL, OrderId BIGINT NOT NULL,
 RefundId BIGINT NOT NULL, RefundAmountIRR BIGINT NOT NULL, ReversedCommissionIRR BIGINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_CommissionReversals_Commission_Refund UNIQUE(CommissionId,RefundId),
 CONSTRAINT CK_CommissionReversals_Amounts CHECK(RefundAmountIRR>0 AND ReversedCommissionIRR>=0 AND ReversedCommissionIRR<=RefundAmountIRR)
);

CREATE TABLE dbo.Campaigns(
 Id BIGINT NOT NULL CONSTRAINT PK_Campaigns PRIMARY KEY, StoreId BIGINT NOT NULL, Name NVARCHAR(250) NOT NULL,
 DiscountType TINYINT NOT NULL, DiscountValue DECIMAL(18,4) NOT NULL, StartsAtUtc DATETIME2(7) NOT NULL,
 EndsAtUtc DATETIME2(7) NOT NULL, IsActive BIT NOT NULL CONSTRAINT DF_Campaigns_IsActive DEFAULT(1),
 CONSTRAINT CK_Campaigns_Dates CHECK(EndsAtUtc>StartsAtUtc),
 CONSTRAINT CK_Campaigns_Discount CHECK(DiscountValue>=0 AND (DiscountType<>1 OR DiscountValue<=100))
);
CREATE TABLE dbo.CampaignProducts(
 Id BIGINT NOT NULL CONSTRAINT PK_CampaignProducts PRIMARY KEY, CampaignId BIGINT NOT NULL, ProductId BIGINT NOT NULL, ProductVariantId BIGINT NULL,
 CONSTRAINT UQ_CampaignProducts UNIQUE(CampaignId,ProductId,ProductVariantId)
);
CREATE TABLE dbo.Coupons(
 Id BIGINT NOT NULL CONSTRAINT PK_Coupons PRIMARY KEY, SellerId BIGINT NOT NULL, StoreId BIGINT NOT NULL,
 Code NVARCHAR(100) NOT NULL, DiscountType TINYINT NOT NULL, DiscountValue DECIMAL(18,4) NOT NULL,
 MaxDiscountAmountIRR BIGINT NULL, MinimumPurchaseIRR BIGINT NULL, MaxUses INT NULL,
 NewCustomerOnly BIT NOT NULL CONSTRAINT DF_Coupons_NewCustomerOnly DEFAULT(0),
 IsActive BIT NOT NULL CONSTRAINT DF_Coupons_IsActive DEFAULT(1), StartsAtUtc DATETIME2(7) NULL, EndsAtUtc DATETIME2(7) NULL,
 CONSTRAINT UQ_Coupons_Store_Code UNIQUE(StoreId,Code),
 CONSTRAINT CK_Coupons_Discount CHECK(DiscountValue>=0 AND (DiscountType<>1 OR DiscountValue<=100)),
 CONSTRAINT CK_Coupons_Limits CHECK((MaxDiscountAmountIRR IS NULL OR MaxDiscountAmountIRR>=0) AND (MinimumPurchaseIRR IS NULL OR MinimumPurchaseIRR>=0) AND (MaxUses IS NULL OR MaxUses>0) AND (EndsAtUtc IS NULL OR StartsAtUtc IS NULL OR EndsAtUtc>StartsAtUtc))
);
CREATE TABLE dbo.CouponProducts(
 Id BIGINT NOT NULL CONSTRAINT PK_CouponProducts PRIMARY KEY, CouponId BIGINT NOT NULL, ProductId BIGINT NOT NULL,
 CONSTRAINT UQ_CouponProducts UNIQUE(CouponId,ProductId)
);
CREATE TABLE dbo.CouponCategories(
 Id BIGINT NOT NULL CONSTRAINT PK_CouponCategories PRIMARY KEY, CouponId BIGINT NOT NULL, CategoryId BIGINT NOT NULL,
 CONSTRAINT UQ_CouponCategories UNIQUE(CouponId,CategoryId)
);
CREATE TABLE dbo.CouponUsages(
 Id BIGINT NOT NULL CONSTRAINT PK_CouponUsages PRIMARY KEY, CouponId BIGINT NOT NULL, CustomerId BIGINT NOT NULL, OrderId BIGINT NOT NULL,
 DiscountAmountIRR BIGINT NOT NULL, CreatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_CouponUsages_Coupon_Customer UNIQUE(CouponId,CustomerId), CONSTRAINT UQ_CouponUsages_Order UNIQUE(OrderId),
 CONSTRAINT CK_CouponUsages_Discount CHECK(DiscountAmountIRR>=0)
);

CREATE TABLE dbo.Notifications(
 Id BIGINT NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY, UserId BIGINT NOT NULL, Channel TINYINT NOT NULL, Status TINYINT NOT NULL,
 Title NVARCHAR(250) NOT NULL, Body NVARCHAR(4000) NOT NULL, ReferenceType NVARCHAR(100) NULL, ReferenceId BIGINT NULL,
 CreatedAtUtc DATETIME2(7) NOT NULL, SentAtUtc DATETIME2(7) NULL, ReadAtUtc DATETIME2(7) NULL
);

CREATE TABLE dbo.SmsProviderSettings(
 Id BIGINT NOT NULL CONSTRAINT PK_SmsProviderSettings PRIMARY KEY, Provider NVARCHAR(50) NOT NULL,
 DisplayName NVARCHAR(150) NOT NULL, IsEnabled BIT NOT NULL CONSTRAINT DF_SmsProviderSettings_IsEnabled DEFAULT(0),
 IsVisible BIT NOT NULL CONSTRAINT DF_SmsProviderSettings_IsVisible DEFAULT(1),
 SortOrder INT NOT NULL CONSTRAINT DF_SmsProviderSettings_SortOrder DEFAULT(0), UpdatedAtUtc DATETIME2(7) NOT NULL,
 CONSTRAINT UQ_SmsProviderSettings_Provider UNIQUE(Provider)
);


-- Foreign keys
ALTER TABLE dbo.UserRules ADD CONSTRAINT FK_UserRules_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
                           CONSTRAINT FK_UserRules_Rules FOREIGN KEY(RuleId) REFERENCES dbo.Rules(Id) ON DELETE CASCADE;
ALTER TABLE dbo.UserRoleAssignments ADD CONSTRAINT FK_UserRoleAssignments_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
                                     CONSTRAINT FK_UserRoleAssignments_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(Id) ON DELETE CASCADE;
ALTER TABLE dbo.Sellers ADD CONSTRAINT FK_Sellers_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id);
ALTER TABLE dbo.Stores ADD CONSTRAINT FK_Stores_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.SellerBankAccounts ADD CONSTRAINT FK_SellerBankAccounts_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.Categories ADD CONSTRAINT FK_Categories_Parent FOREIGN KEY(ParentCategoryId) REFERENCES dbo.Categories(Id);
ALTER TABLE dbo.Products ADD CONSTRAINT FK_Products_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id),
                           CONSTRAINT FK_Products_Categories FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(Id);
ALTER TABLE dbo.ProductVariants ADD CONSTRAINT FK_ProductVariants_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id);
ALTER TABLE dbo.ProductAttributes ADD CONSTRAINT FK_ProductAttributes_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
ALTER TABLE dbo.ProductAttributeValues ADD CONSTRAINT FK_ProductAttributeValues_Attributes FOREIGN KEY(ProductAttributeId) REFERENCES dbo.ProductAttributes(Id);
ALTER TABLE dbo.ProductAttributeAssignments ADD CONSTRAINT FK_ProductAttributeAssignments_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
                                             CONSTRAINT FK_ProductAttributeAssignments_Attributes FOREIGN KEY(ProductAttributeId) REFERENCES dbo.ProductAttributes(Id);
ALTER TABLE dbo.VariantAttributeValues ADD CONSTRAINT FK_VariantAttributeValues_Variants FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id),
                                         CONSTRAINT FK_VariantAttributeValues_Values FOREIGN KEY(ProductAttributeValueId) REFERENCES dbo.ProductAttributeValues(Id);
ALTER TABLE dbo.Warranties ADD CONSTRAINT FK_Warranties_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
ALTER TABLE dbo.ProductWarranties ADD CONSTRAINT FK_ProductWarranties_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
                                   CONSTRAINT FK_ProductWarranties_Warranties FOREIGN KEY(WarrantyId) REFERENCES dbo.Warranties(Id);
ALTER TABLE dbo.Carts ADD CONSTRAINT FK_Carts_Users FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id),
                       CONSTRAINT FK_Carts_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id),
                       CONSTRAINT FK_Carts_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.CartItems ADD CONSTRAINT FK_CartItems_Carts FOREIGN KEY(CartId) REFERENCES dbo.Carts(Id) ON DELETE CASCADE,
                           CONSTRAINT FK_CartItems_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
                           CONSTRAINT FK_CartItems_Variants FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id),
                           CONSTRAINT FK_CartItems_Warranties FOREIGN KEY(WarrantyId) REFERENCES dbo.Warranties(Id);
ALTER TABLE dbo.StoreShippingCities ADD CONSTRAINT FK_StoreShippingCities_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id) ON DELETE CASCADE,
                                    CONSTRAINT FK_StoreShippingCities_DeliveryCities FOREIGN KEY(CityId) REFERENCES dbo.DeliveryCities(Id);
ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id),
                        CONSTRAINT FK_Orders_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
                        CONSTRAINT FK_Orders_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id),
                        CONSTRAINT FK_Orders_DestinationCity FOREIGN KEY(DestinationCityId) REFERENCES dbo.DeliveryCities(Id);
ALTER TABLE dbo.OrderItems ADD CONSTRAINT FK_OrderItems_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
                            CONSTRAINT FK_OrderItems_Products FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
                            CONSTRAINT FK_OrderItems_Variants FOREIGN KEY(VariantId) REFERENCES dbo.ProductVariants(Id),
                            CONSTRAINT FK_OrderItems_Warranties FOREIGN KEY(WarrantyId) REFERENCES dbo.Warranties(Id),
                            CONSTRAINT FK_OrderItems_Campaigns FOREIGN KEY(CampaignId) REFERENCES dbo.Campaigns(Id);
ALTER TABLE dbo.Payments ADD CONSTRAINT FK_Payments_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                          CONSTRAINT FK_Payments_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id);
ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_Payments FOREIGN KEY(PaymentId) REFERENCES dbo.Payments(Id) ON DELETE CASCADE;
ALTER TABLE dbo.Deliveries ADD CONSTRAINT FK_Deliveries_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                           CONSTRAINT FK_Deliveries_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.Shipments ADD CONSTRAINT FK_Shipments_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                         CONSTRAINT FK_Shipments_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.ShipmentTrackingEvents ADD CONSTRAINT FK_ShipmentTrackingEvents_Shipments FOREIGN KEY(ShipmentId) REFERENCES dbo.Shipments(Id),
                                        CONSTRAINT FK_ShipmentTrackingEvents_Users FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(Id);
ALTER TABLE dbo.DeliveryCodes ADD CONSTRAINT FK_DeliveryCodes_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id);
ALTER TABLE dbo.Refunds ADD CONSTRAINT FK_Refunds_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                        CONSTRAINT FK_Refunds_Payments FOREIGN KEY(PaymentId) REFERENCES dbo.Payments(Id),
                        CONSTRAINT FK_Refunds_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id);
ALTER TABLE dbo.Complaints ADD CONSTRAINT FK_Complaints_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                           CONSTRAINT FK_Complaints_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id),
                           CONSTRAINT FK_Complaints_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.InventoryItems ADD CONSTRAINT FK_InventoryItems_Variants FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id);
ALTER TABLE dbo.InventoryReservations ADD CONSTRAINT FK_InventoryReservations_Variants FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id),
                                       CONSTRAINT FK_InventoryReservations_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id);
ALTER TABLE dbo.SellerBalances ADD CONSTRAINT FK_SellerBalances_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.SellerBalanceHolds ADD CONSTRAINT FK_SellerBalanceHolds_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
                                     CONSTRAINT FK_SellerBalanceHolds_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id);
ALTER TABLE dbo.Settlements ADD CONSTRAINT FK_Settlements_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
                             CONSTRAINT FK_Settlements_BankAccounts FOREIGN KEY(BankAccountId) REFERENCES dbo.SellerBankAccounts(Id);
ALTER TABLE dbo.BalanceTransactions ADD CONSTRAINT FK_BalanceTransactions_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
                                     CONSTRAINT FK_BalanceTransactions_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                                     CONSTRAINT FK_BalanceTransactions_Settlements FOREIGN KEY(SettlementId) REFERENCES dbo.Settlements(Id),
                                     CONSTRAINT FK_BalanceTransactions_Refunds FOREIGN KEY(RefundId) REFERENCES dbo.Refunds(Id);
ALTER TABLE dbo.Commissions ADD CONSTRAINT FK_Commissions_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                            CONSTRAINT FK_Commissions_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id),
                            CONSTRAINT FK_Commissions_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
ALTER TABLE dbo.CommissionReversals ADD CONSTRAINT FK_CommissionReversals_Commissions FOREIGN KEY(CommissionId) REFERENCES dbo.Commissions(Id),
                                      CONSTRAINT FK_CommissionReversals_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id),
                                      CONSTRAINT FK_CommissionReversals_Refunds FOREIGN KEY(RefundId) REFERENCES dbo.Refunds(Id);
ALTER TABLE dbo.Campaigns ADD CONSTRAINT FK_Campaigns_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
ALTER TABLE dbo.CampaignProducts ADD CONSTRAINT FK_CampaignProducts_Campaign FOREIGN KEY(CampaignId) REFERENCES dbo.Campaigns(Id) ON DELETE CASCADE,
                                  CONSTRAINT FK_CampaignProducts_Product FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
                                  CONSTRAINT FK_CampaignProducts_Variant FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id);
ALTER TABLE dbo.Coupons ADD CONSTRAINT FK_Coupons_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id),
                         CONSTRAINT FK_Coupons_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
ALTER TABLE dbo.CouponProducts ADD CONSTRAINT FK_CouponProducts_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
                                CONSTRAINT FK_CouponProducts_Product FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id);
ALTER TABLE dbo.CouponCategories ADD CONSTRAINT FK_CouponCategories_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
                                  CONSTRAINT FK_CouponCategories_Category FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(Id);
ALTER TABLE dbo.CouponUsages ADD CONSTRAINT FK_CouponUsages_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
                              CONSTRAINT FK_CouponUsages_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Users(Id),
                              CONSTRAINT FK_CouponUsages_Orders FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id);
ALTER TABLE dbo.Notifications ADD CONSTRAINT FK_Notifications_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE;

-- Indexes
CREATE INDEX IX_Stores_SellerId ON dbo.Stores(SellerId);
CREATE INDEX IX_SellerBankAccounts_Seller_Default ON dbo.SellerBankAccounts(SellerId,IsDefault);
CREATE INDEX IX_Categories_Path ON dbo.Categories(Path);
CREATE INDEX IX_Products_Store_Status ON dbo.Products(StoreId,Status);
CREATE INDEX IX_ProductVariants_Product ON dbo.ProductVariants(ProductId);
CREATE INDEX IX_ProductAttributeValues_Attribute ON dbo.ProductAttributeValues(ProductAttributeId);
CREATE INDEX IX_ProductAttributeAssignments_Attribute ON dbo.ProductAttributeAssignments(ProductAttributeId);
CREATE INDEX IX_VariantAttributeValues_Value ON dbo.VariantAttributeValues(ProductAttributeValueId);
CREATE INDEX IX_ProductWarranties_Warranty ON dbo.ProductWarranties(WarrantyId);
CREATE INDEX IX_Carts_Store_Customer ON dbo.Carts(StoreId,CustomerId);
CREATE INDEX IX_CartItems_Cart ON dbo.CartItems(CartId);
CREATE INDEX IX_StoreShippingCities_StoreId ON dbo.StoreShippingCities(StoreId);
CREATE INDEX IX_StoreShippingCities_CityId ON dbo.StoreShippingCities(CityId);
CREATE INDEX IX_StoreShippingRates_CityId ON dbo.StoreShippingRates(CityId);
CREATE INDEX IX_Orders_Seller_Status ON dbo.Orders(SellerId,Status);
CREATE INDEX IX_Orders_Customer_Created ON dbo.Orders(CustomerId,CreatedAtUtc);
CREATE INDEX IX_Orders_DestinationCityId ON dbo.Orders(DestinationCityId);
CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems(OrderId);
CREATE INDEX IX_Payments_Authority ON dbo.Payments(Authority);
CREATE INDEX IX_PaymentReconciliationAudits_PaymentId_CreatedAtUtc ON dbo.PaymentReconciliationAudits(PaymentId,CreatedAtUtc);
CREATE INDEX IX_PaymentTransactions_Payment_Status ON dbo.PaymentTransactions(PaymentId,Status);
CREATE UNIQUE INDEX UX_PaymentTransactions_Provider_Authority ON dbo.PaymentTransactions(Provider,Authority) WHERE Authority IS NOT NULL;
CREATE INDEX IX_Deliveries_Status_Expires ON dbo.Deliveries(Status,ExpiresAtUtc);
CREATE INDEX IX_Shipments_Seller_Status_Updated ON dbo.Shipments(SellerId,Status,UpdatedAtUtc);
CREATE INDEX IX_ShipmentTrackingEvents_Shipment_Occurred ON dbo.ShipmentTrackingEvents(ShipmentId,OccurredAtUtc,Id);
CREATE INDEX IX_DeliveryCodes_Expiry ON dbo.DeliveryCodes(ExpiresAtUtc,UsedAtUtc);
CREATE INDEX IX_Refunds_Order_Status ON dbo.Refunds(OrderId,Status);
CREATE UNIQUE INDEX UX_Refunds_OneActivePerOrder ON dbo.Refunds(OrderId) WHERE Status < 4;
CREATE INDEX IX_RefundReconciliationAudits_Refund_Created ON dbo.RefundReconciliationAudits(RefundId,CreatedAtUtc);
CREATE INDEX IX_Complaints_Order_Status ON dbo.Complaints(OrderId,Status);
-- Database-level protection against two concurrent active complaints for one order.
-- Resolved/cancelled complaints release the slot for valid future lifecycle cases.
CREATE UNIQUE INDEX UX_Complaints_OneActivePerOrder ON dbo.Complaints(OrderId) WHERE Status IN (1, 2);
CREATE INDEX IX_InventoryReservations_Order_Status ON dbo.InventoryReservations(OrderId,Status);
CREATE INDEX IX_InventoryReservations_Status_Expiry ON dbo.InventoryReservations(Status,ExpiresAtUtc);
CREATE INDEX IX_SellerBalanceHolds_Order_Status ON dbo.SellerBalanceHolds(OrderId,Status);
CREATE UNIQUE INDEX UX_SellerBalanceHolds_OrderId ON dbo.SellerBalanceHolds(OrderId) WHERE OrderId IS NOT NULL;
CREATE INDEX IX_BalanceTransactions_Seller_Created ON dbo.BalanceTransactions(SellerId,CreatedAtUtc);
CREATE INDEX IX_BalanceTransactions_Order_Type ON dbo.BalanceTransactions(OrderId,Type);
CREATE UNIQUE INDEX UX_BalanceTransactions_Order_Sale ON dbo.BalanceTransactions(OrderId) WHERE OrderId IS NOT NULL AND Type = 1;
CREATE UNIQUE INDEX UX_BalanceTransactions_RefundId ON dbo.BalanceTransactions(RefundId) WHERE RefundId IS NOT NULL;
CREATE INDEX IX_SettlementReconciliationAudits_Settlement_Created ON dbo.SettlementReconciliationAudits(SettlementId,CreatedAtUtc);
CREATE UNIQUE INDEX UX_Settlements_Seller_RequestKey ON dbo.Settlements(SellerId,RequestKey) WHERE RequestKey IS NOT NULL;
CREATE INDEX IX_Settlements_Seller_Status ON dbo.Settlements(SellerId,Status);
CREATE INDEX IX_Settlements_Status_RequestedAt ON dbo.Settlements(Status,RequestedAtUtc);
CREATE INDEX IX_Campaigns_Store_Active ON dbo.Campaigns(StoreId,IsActive);
CREATE INDEX IX_Campaigns_Store_Dates ON dbo.Campaigns(StoreId,StartsAtUtc,EndsAtUtc);
CREATE INDEX IX_CampaignProducts_Product ON dbo.CampaignProducts(ProductId,ProductVariantId);
CREATE INDEX IX_Coupons_Store_Active ON dbo.Coupons(StoreId,IsActive);
CREATE INDEX IX_CouponUsages_Coupon ON dbo.CouponUsages(CouponId);
CREATE INDEX IX_Notifications_User_Status_Created ON dbo.Notifications(UserId,Status,CreatedAtUtc DESC);
CREATE INDEX IX_SmsProviderSettings_Enabled_Visible_Sort ON dbo.SmsProviderSettings(IsEnabled,IsVisible,SortOrder);

-- Seed roles, rules and payment providers.
IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Id=1) INSERT dbo.Roles(Id,Name) VALUES(1,N'Customer');
IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Id=2) INSERT dbo.Roles(Id,Name) VALUES(2,N'Seller');
IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Id=3) INSERT dbo.Roles(Id,Name) VALUES(3,N'Admin');

MERGE dbo.Rules AS t USING (VALUES
(1001,N'Cart.Read',N'View own cart',1),(1002,N'Order.Create',N'Create order',1),(1003,N'Order.ReadOwn',N'View own orders',1),
(2001,N'Seller.Shipping.Configure',N'Configure store shipping cities',2),(2002,N'Seller.Settlement.Request',N'Request seller settlement',2),
(2003,N'Seller.Campaign.Manage',N'Manage store campaigns',2),(2004,N'Seller.Coupon.Manage',N'Manage store coupons',2),(2005,N'Seller.Catalog.Manage',N'Manage store catalog',2),
(3001,N'Admin.PaymentProviders.Read',N'View payment provider settings',3),(3002,N'Admin.PaymentProviders.Configure',N'Configure payment providers',3),
(3003,N'Admin.Settlement.Process',N'Process seller settlements',3),(3004,N'Admin.Identity.Manage',N'Manage users roles and permissions',3),(3005,N'Admin.Seller.Manage',N'Manage sellers',3),
(4001,N'Order.Delivery.Confirm',N'Confirm delivery',4),(4002,N'Complaint.Resolve',N'Resolve complaint',4),
(3006,N'Admin.SmsProviders.Read',N'View SMS provider settings',3),(3007,N'Admin.SmsProviders.Configure',N'Configure SMS provider settings',3),(3008,N'Admin.Order.Read',N'Read and investigate marketplace orders',3)
) AS s(Id,Code,Name,ActionType)
ON t.Id=s.Id
WHEN MATCHED THEN UPDATE SET Code=s.Code,Name=s.Name,ActionType=s.ActionType,IsActive=1
WHEN NOT MATCHED THEN INSERT(Id,Code,Name,ActionType,IsActive) VALUES(s.Id,s.Code,s.Name,s.ActionType,1);

MERGE dbo.PaymentProviderSettings AS t
USING (VALUES
(1000000101,1,N'بانک ملی',1),(1000000102,2,N'بانک پارسیان',2),(1000000103,3,N'بانک پاسارگاد',3),
(1000000104,4,N'بانک ملت',4),(1000000105,5,N'بانک سپه',5),(1000000106,6,N'بانک تجارت',6),(1000000107,7,N'بانک سامان',7),
(1000000108,8,N'بانک تستی (بدون اتصال به بانک واقعی)',0)
) AS s(Id,Provider,DisplayName,SortOrder)
ON t.Provider=s.Provider
WHEN NOT MATCHED THEN INSERT(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder,ConfigurationJson,UpdatedAtUtc)
VALUES(s.Id,s.Provider,s.DisplayName,CASE WHEN s.Provider=8 THEN 1 ELSE 0 END,CASE WHEN s.Provider=8 THEN 1 ELSE 0 END,s.SortOrder,N'{}',SYSUTCDATETIME());

MERGE dbo.SmsProviderSettings AS t
USING (VALUES
(1,N'Test',N'سامانه پیامکی تست',0,1,1),
(2,N'Kavenegar',N'کاوه نگار',0,1,2),
(3,N'Melipayamak',N'ملی پیامک',0,1,3),
(4,N'SmsIr',N'SMS.ir',0,1,4),
(5,N'HttpApi',N'سرویس پیامکی HTTP API',0,1,5)
) AS s(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder)
ON t.Provider=s.Provider
WHEN MATCHED THEN UPDATE SET DisplayName=s.DisplayName,IsVisible=s.IsVisible,SortOrder=s.SortOrder
WHEN NOT MATCHED THEN INSERT(Id,Provider,DisplayName,IsEnabled,IsVisible,SortOrder,UpdatedAtUtc)
VALUES(s.Id,s.Provider,s.DisplayName,s.IsEnabled,s.IsVisible,s.SortOrder,SYSUTCDATETIME());

COMMIT;

-- Fail the script if any required table was not created.
DECLARE @ExpectedTables TABLE (TableName SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables(TableName) VALUES
(N'Users'),
(N'Roles'),
(N'Rules'),
(N'UserRules'),
(N'UserRoleAssignments'),
(N'Sellers'),
(N'Stores'),
(N'SellerBankAccounts'),
(N'Categories'),
(N'Products'),
(N'ProductVariants'),
(N'ProductAttributes'),
(N'ProductAttributeValues'),
(N'ProductAttributeAssignments'),
(N'VariantAttributeValues'),
(N'Warranties'),
(N'ProductWarranties'),
(N'Carts'),
(N'CartItems'),
(N'DeliveryCities'),
(N'StoreShippingCities'),
(N'StoreShippingRates'),
(N'Orders'),
(N'OrderItems'),
(N'Payments'),
(N'PaymentTransactions'),
(N'PaymentProviderSettings'),
(N'Deliveries'),
(N'Shipments'),
(N'ShipmentTrackingEvents'),
(N'DeliveryCodes'),
(N'Refunds'),
(N'Complaints'),
(N'InventoryItems'),
(N'InventoryReservations'),
(N'SellerBalances'),
(N'SellerBalanceHolds'),
(N'Settlements'),
(N'BalanceTransactions'),
(N'Commissions'),
(N'CommissionReversals'),
(N'Campaigns'),
(N'CampaignProducts'),
(N'Coupons'),
(N'CouponProducts'),
(N'CouponCategories'),
(N'CouponUsages'),
(N'Notifications'),
(N'SmsProviderSettings'),
(N'PaymentReconciliationAudits'),
(N'RefundReconciliationAudits'),
(N'SettlementReconciliationAudits'),
(N'OutboxMessages'),
(N'OutboxMessageArchive'),
(N'AdminAuditEvents');

IF EXISTS (
    SELECT 1
    FROM @ExpectedTables e
    LEFT JOIN sys.tables t ON t.name=e.TableName AND t.schema_id=SCHEMA_ID(N'dbo')
    WHERE t.object_id IS NULL
)
    THROW 51000, 'Marketplace schema validation failed: one or more required tables are missing.', 1;

SELECT t.name AS TableName
FROM sys.tables t
INNER JOIN @ExpectedTables e ON e.TableName=t.name
WHERE t.schema_id=SCHEMA_ID(N'dbo')
ORDER BY t.name;

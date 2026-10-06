IF OBJECT_ID(N'dbo.Campaigns',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.Campaigns(
  Id BIGINT NOT NULL CONSTRAINT PK_Campaigns PRIMARY KEY,
  StoreId BIGINT NOT NULL,
  Name NVARCHAR(250) NOT NULL,
  DiscountType TINYINT NOT NULL,
  DiscountValue DECIMAL(18,4) NOT NULL,
  StartsAtUtc DATETIME2 NOT NULL,
  EndsAtUtc DATETIME2 NOT NULL,
  IsActive BIT NOT NULL CONSTRAINT DF_Campaigns_IsActive DEFAULT(1),
  CONSTRAINT CK_Campaigns_Dates CHECK(EndsAtUtc>StartsAtUtc),
  CONSTRAINT CK_Campaigns_Discount CHECK(DiscountValue>=0 AND (DiscountType<>1 OR DiscountValue<=100))
 );
 CREATE INDEX IX_Campaigns_Store_Active ON dbo.Campaigns(StoreId,IsActive);
 CREATE INDEX IX_Campaigns_Store_Dates ON dbo.Campaigns(StoreId,StartsAtUtc,EndsAtUtc);
END
GO
IF OBJECT_ID(N'dbo.CampaignProducts',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.CampaignProducts(
  Id BIGINT NOT NULL CONSTRAINT PK_CampaignProducts PRIMARY KEY,
  CampaignId BIGINT NOT NULL,
  ProductId BIGINT NOT NULL,
  ProductVariantId BIGINT NULL,
  CONSTRAINT UQ_CampaignProducts UNIQUE(CampaignId,ProductId,ProductVariantId),
  CONSTRAINT FK_CampaignProducts_Campaign FOREIGN KEY(CampaignId) REFERENCES dbo.Campaigns(Id) ON DELETE CASCADE,
  CONSTRAINT FK_CampaignProducts_Product FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id),
  CONSTRAINT FK_CampaignProducts_Variant FOREIGN KEY(ProductVariantId) REFERENCES dbo.ProductVariants(Id)
 );
 CREATE INDEX IX_CampaignProducts_Product ON dbo.CampaignProducts(ProductId,ProductVariantId);
END
GO
IF OBJECT_ID(N'dbo.Coupons',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.Coupons(
  Id BIGINT NOT NULL CONSTRAINT PK_Coupons PRIMARY KEY,
  SellerId BIGINT NOT NULL,
  StoreId BIGINT NOT NULL,
  Code NVARCHAR(100) NOT NULL,
  DiscountType TINYINT NOT NULL,
  DiscountValue DECIMAL(18,4) NOT NULL,
  MaxDiscountAmountIRR BIGINT NULL,
  MinimumPurchaseIRR BIGINT NULL,
  MaxUses INT NULL,
  NewCustomerOnly BIT NOT NULL CONSTRAINT DF_Coupons_NewCustomerOnly DEFAULT(0),
  IsActive BIT NOT NULL CONSTRAINT DF_Coupons_IsActive DEFAULT(1),
  StartsAtUtc DATETIME2 NULL,
  EndsAtUtc DATETIME2 NULL,
  CONSTRAINT UQ_Coupons_Store_Code UNIQUE(StoreId,Code),
  CONSTRAINT CK_Coupons_Discount CHECK(DiscountValue>=0 AND (DiscountType<>1 OR DiscountValue<=100)),
  CONSTRAINT CK_Coupons_Limits CHECK((MaxDiscountAmountIRR IS NULL OR MaxDiscountAmountIRR>=0) AND (MinimumPurchaseIRR IS NULL OR MinimumPurchaseIRR>=0) AND (MaxUses IS NULL OR MaxUses>0) AND (EndsAtUtc IS NULL OR StartsAtUtc IS NULL OR EndsAtUtc>StartsAtUtc))
 );
 CREATE INDEX IX_Coupons_Store_Active ON dbo.Coupons(StoreId,IsActive);
END
GO
IF OBJECT_ID(N'dbo.CouponProducts',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.CouponProducts(
  Id BIGINT NOT NULL CONSTRAINT PK_CouponProducts PRIMARY KEY,
  CouponId BIGINT NOT NULL,
  ProductId BIGINT NOT NULL,
  CONSTRAINT UQ_CouponProducts UNIQUE(CouponId,ProductId),
  CONSTRAINT FK_CouponProducts_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
  CONSTRAINT FK_CouponProducts_Product FOREIGN KEY(ProductId) REFERENCES dbo.Products(Id)
 );
END
GO
IF OBJECT_ID(N'dbo.CouponCategories',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.CouponCategories(
  Id BIGINT NOT NULL CONSTRAINT PK_CouponCategories PRIMARY KEY,
  CouponId BIGINT NOT NULL,
  CategoryId BIGINT NOT NULL,
  CONSTRAINT UQ_CouponCategories UNIQUE(CouponId,CategoryId),
  CONSTRAINT FK_CouponCategories_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
  CONSTRAINT FK_CouponCategories_Category FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(Id)
 );
END
GO
IF OBJECT_ID(N'dbo.CouponUsages',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.CouponUsages(
  Id BIGINT NOT NULL CONSTRAINT PK_CouponUsages PRIMARY KEY,
  CouponId BIGINT NOT NULL,
  CustomerId BIGINT NOT NULL,
  OrderId BIGINT NOT NULL,
  DiscountAmountIRR BIGINT NOT NULL,
  CreatedAtUtc DATETIME2 NOT NULL,
  CONSTRAINT UQ_CouponUsages_Coupon_Customer UNIQUE(CouponId,CustomerId),
  CONSTRAINT UQ_CouponUsages_Order UNIQUE(OrderId),
  CONSTRAINT FK_CouponUsages_Coupon FOREIGN KEY(CouponId) REFERENCES dbo.Coupons(Id) ON DELETE CASCADE,
  CONSTRAINT FK_CouponUsages_Order FOREIGN KEY(OrderId) REFERENCES dbo.Orders(Id)
 );
 CREATE INDEX IX_CouponUsages_Coupon ON dbo.CouponUsages(CouponId);
END
GO
IF COL_LENGTH(N'dbo.Orders',N'SubtotalAmountIRR') IS NULL ALTER TABLE dbo.Orders ADD SubtotalAmountIRR BIGINT NOT NULL CONSTRAINT DF_Orders_SubtotalAmountIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.Orders',N'CampaignDiscountIRR') IS NULL ALTER TABLE dbo.Orders ADD CampaignDiscountIRR BIGINT NOT NULL CONSTRAINT DF_Orders_CampaignDiscountIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.Orders',N'CouponDiscountIRR') IS NULL ALTER TABLE dbo.Orders ADD CouponDiscountIRR BIGINT NOT NULL CONSTRAINT DF_Orders_CouponDiscountIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.Orders',N'CouponCodeSnapshot') IS NULL ALTER TABLE dbo.Orders ADD CouponCodeSnapshot NVARCHAR(100) NULL;
GO
IF COL_LENGTH(N'dbo.OrderItems',N'BaseUnitPriceIRR') IS NULL ALTER TABLE dbo.OrderItems ADD BaseUnitPriceIRR BIGINT NOT NULL CONSTRAINT DF_OrderItems_BaseUnitPriceIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.OrderItems',N'CampaignDiscountIRR') IS NULL ALTER TABLE dbo.OrderItems ADD CampaignDiscountIRR BIGINT NOT NULL CONSTRAINT DF_OrderItems_CampaignDiscountIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.OrderItems',N'CouponDiscountIRR') IS NULL ALTER TABLE dbo.OrderItems ADD CouponDiscountIRR BIGINT NOT NULL CONSTRAINT DF_OrderItems_CouponDiscountIRR DEFAULT(0);
IF COL_LENGTH(N'dbo.OrderItems',N'CampaignId') IS NULL ALTER TABLE dbo.OrderItems ADD CampaignId BIGINT NULL;
IF COL_LENGTH(N'dbo.OrderItems',N'CampaignNameSnapshot') IS NULL ALTER TABLE dbo.OrderItems ADD CampaignNameSnapshot NVARCHAR(250) NULL;
GO

GO
UPDATE dbo.Orders SET SubtotalAmountIRR=TotalAmountIRR WHERE SubtotalAmountIRR=0 AND TotalAmountIRR>0;
UPDATE dbo.OrderItems SET BaseUnitPriceIRR=UnitPriceIRR WHERE BaseUnitPriceIRR=0 AND UnitPriceIRR>0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Campaigns_Stores')
    ALTER TABLE dbo.Campaigns ADD CONSTRAINT FK_Campaigns_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Coupons_Sellers')
    ALTER TABLE dbo.Coupons ADD CONSTRAINT FK_Coupons_Sellers FOREIGN KEY(SellerId) REFERENCES dbo.Sellers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Coupons_Stores')
    ALTER TABLE dbo.Coupons ADD CONSTRAINT FK_Coupons_Stores FOREIGN KEY(StoreId) REFERENCES dbo.Stores(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_OrderItems_Campaigns')
    ALTER TABLE dbo.OrderItems ADD CONSTRAINT FK_OrderItems_Campaigns FOREIGN KEY(CampaignId) REFERENCES dbo.Campaigns(Id);
GO

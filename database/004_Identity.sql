IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id BIGINT NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Mobile NVARCHAR(30) NOT NULL,
        Email NVARCHAR(320) NULL,
        PasswordHash NVARCHAR(500) NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT(1),
        IsMobileVerified BIT NOT NULL CONSTRAINT DF_Users_IsMobileVerified DEFAULT(0),
        CreatedAtUtc DATETIME2(7) NOT NULL,
        LastLoginAtUtc DATETIME2(7) NULL,
        CONSTRAINT UQ_Users_Mobile UNIQUE(Mobile),
    );
    CREATE UNIQUE INDEX UX_Users_Email_NotNull ON dbo.Users(Email) WHERE Email IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Roles
    (
        Id BIGINT NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE,
        IsActive BIT NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT(1)
    );
END;
GO

IF OBJECT_ID(N'dbo.Rules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rules
    (
        Id BIGINT NOT NULL CONSTRAINT PK_Rules PRIMARY KEY,
        Code NVARCHAR(150) NOT NULL CONSTRAINT UQ_Rules_Code UNIQUE,
        Name NVARCHAR(250) NOT NULL,
        ActionType TINYINT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Rules_IsActive DEFAULT(1)
    );
END;
GO

IF OBJECT_ID(N'dbo.UserRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRules
    (
        Id BIGINT NOT NULL CONSTRAINT PK_UserRules PRIMARY KEY,
        UserId BIGINT NOT NULL,
        RuleId BIGINT NOT NULL,
        GrantedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT FK_UserRules_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserRules_Rules FOREIGN KEY(RuleId) REFERENCES dbo.Rules(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_UserRules_User_Rule UNIQUE(UserId, RuleId)
    );
END;
GO

IF OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRoleAssignments
    (
        Id BIGINT NOT NULL CONSTRAINT PK_UserRoleAssignments PRIMARY KEY,
        UserId BIGINT NOT NULL,
        RoleId BIGINT NOT NULL,
        CONSTRAINT FK_UserRoleAssignments_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserRoleAssignments_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_UserRoleAssignments_User_Role UNIQUE(UserId, RoleId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id=1) INSERT dbo.Roles(Id,Name) VALUES (1,N'Customer');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id=2) INSERT dbo.Roles(Id,Name) VALUES (2,N'Seller');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id=3) INSERT dbo.Roles(Id,Name) VALUES (3,N'Admin');
GO

MERGE dbo.Rules AS target
USING (VALUES
 (1001,N'Cart.Read',N'View own cart',1),
 (1002,N'Order.Create',N'Create order',1),
 (1003,N'Order.ReadOwn',N'View own orders',1),
 (2001,N'Seller.Shipping.Configure',N'Configure store shipping cities',2),
 (2003,N'Seller.Campaign.Manage',N'Manage store campaigns',2),
 (2004,N'Seller.Coupon.Manage',N'Manage store coupons',2),
 (2002,N'Seller.Settlement.Request',N'Request seller settlement',2),
 (3001,N'Admin.PaymentProviders.Read',N'View payment provider settings',3),
 (3002,N'Admin.PaymentProviders.Configure',N'Configure payment providers',3),
 (3003,N'Admin.Settlement.Process',N'Process seller settlements',3),
 (4001,N'Order.Delivery.Confirm',N'Confirm delivery',4),
 (4002,N'Complaint.Resolve',N'Resolve complaint',4),
 (3004,N'Admin.Identity.Manage',N'Manage users roles and permissions',3),
 (3005,N'Admin.Seller.Manage',N'Manage sellers',3)
) AS source(Id,Code,Name,ActionType)
ON target.Id=source.Id
WHEN MATCHED THEN UPDATE SET Code=source.Code,Name=source.Name,ActionType=source.ActionType,IsActive=1
WHEN NOT MATCHED THEN INSERT(Id,Code,Name,ActionType,IsActive) VALUES(source.Id,source.Code,source.Name,source.ActionType,1);
GO
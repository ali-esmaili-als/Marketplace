/*
 Marketplace Authorization bootstrap.
 Run after the core Marketplace schema.
 UserTypeId: 1=Admin, 2=Seller, 3=Customer.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Permissions(
        Id BIGINT NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
        Code VARCHAR(150) NOT NULL,
        Name NVARCHAR(250) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Permissions_IsActive DEFAULT(1),
        CONSTRAINT UQ_Permissions_Code UNIQUE(Code)
    );
END;

IF OBJECT_ID(N'dbo.Rules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rules(
        Id BIGINT NOT NULL CONSTRAINT PK_Rules PRIMARY KEY,
        Code VARCHAR(150) NOT NULL,
        Name NVARCHAR(250) NOT NULL,
        RuleType TINYINT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Rules_IsActive DEFAULT(1),
        CONSTRAINT UQ_Rules_Code UNIQUE(Code)
    );
END;

IF OBJECT_ID(N'dbo.UserRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRules(
        Id BIGINT NOT NULL CONSTRAINT PK_UserRules PRIMARY KEY,
        UserId BIGINT NOT NULL,
        RuleId BIGINT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_UserRules_IsActive DEFAULT(1),
        CONSTRAINT FK_UserRules_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserRules_Rules FOREIGN KEY(RuleId) REFERENCES dbo.Rules(Id),
        CONSTRAINT UQ_UserRules_User_Rule UNIQUE(UserId,RuleId)
    );
END;

IF OBJECT_ID(N'dbo.RulePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RulePermissions(
        Id BIGINT NOT NULL CONSTRAINT PK_RulePermissions PRIMARY KEY,
        RuleId BIGINT NOT NULL,
        PermissionId BIGINT NOT NULL,
        CONSTRAINT FK_RulePermissions_Rules FOREIGN KEY(RuleId) REFERENCES dbo.Rules(Id),
        CONSTRAINT FK_RulePermissions_Permissions FOREIGN KEY(PermissionId) REFERENCES dbo.Permissions(Id),
        CONSTRAINT UQ_RulePermissions_Rule_Permission UNIQUE(RuleId,PermissionId)
    );
END;

IF OBJECT_ID(N'dbo.PermissionUserTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PermissionUserTypes(
        Id BIGINT NOT NULL CONSTRAINT PK_PermissionUserTypes PRIMARY KEY,
        PermissionId BIGINT NOT NULL,
        UserTypeId TINYINT NOT NULL,
        CONSTRAINT FK_PermissionUserTypes_Permissions FOREIGN KEY(PermissionId) REFERENCES dbo.Permissions(Id),
        CONSTRAINT FK_PermissionUserTypes_UserTypes FOREIGN KEY(UserTypeId) REFERENCES dbo.UserTypes(Id),
        CONSTRAINT UQ_PermissionUserTypes_Permission_Type UNIQUE(PermissionId,UserTypeId)
    );
END;

MERGE dbo.Permissions AS t
USING (VALUES
 (1001,'Checkout.Pay',N'پرداخت Checkout'),
 (1002,'Delivery.Confirm',N'تأیید تحویل'),
 (1003,'Refund.Create',N'ثبت درخواست بازپرداخت'),
 (1004,'Complaint.Open',N'ثبت شکایت'),
 (1005,'Complaint.Resolve.Customer',N'حل شکایت به نفع مشتری'),
 (1006,'Complaint.Resolve.Seller',N'حل شکایت به نفع فروشنده'),
 (1007,'Complaint.Close',N'بستن شکایت'),
 (1008,'Settlement.Request',N'درخواست تسویه')
) AS s(Id,Code,Name)
ON t.Code=s.Code
WHEN MATCHED THEN UPDATE SET Name=s.Name,IsActive=1
WHEN NOT MATCHED THEN INSERT(Id,Code,Name,IsActive) VALUES(s.Id,s.Code,s.Name,1);

MERGE dbo.PermissionUserTypes AS t
USING (VALUES
 (1001,1),(1002,1),(1003,1),(1004,1),(1005,1),(1006,1),(1007,1),(1008,1),
 (1001,3),(1002,2),(1002,3),(1003,3),(1004,3),(1005,3),(1006,2),(1007,2),(1007,3),(1008,2)
) AS s(PermissionId,UserTypeId)
ON t.PermissionId=s.PermissionId AND t.UserTypeId=s.UserTypeId
WHEN NOT MATCHED THEN
 INSERT(Id,PermissionId,UserTypeId)
 VALUES((s.PermissionId * 10) + s.UserTypeId,s.PermissionId,s.UserTypeId);

COMMIT TRANSACTION;

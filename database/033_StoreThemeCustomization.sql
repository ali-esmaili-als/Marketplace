SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Stores', N'ThemePrimaryColor') IS NULL
    ALTER TABLE dbo.Stores ADD ThemePrimaryColor CHAR(7) NOT NULL CONSTRAINT DF_Stores_ThemePrimaryColor DEFAULT('#3159c9');
IF COL_LENGTH(N'dbo.Stores', N'ThemeSecondaryColor') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeSecondaryColor CHAR(7) NOT NULL CONSTRAINT DF_Stores_ThemeSecondaryColor DEFAULT('#f3f7ff');
IF COL_LENGTH(N'dbo.Stores', N'ThemeBackgroundColor') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeBackgroundColor CHAR(7) NOT NULL CONSTRAINT DF_Stores_ThemeBackgroundColor DEFAULT('#ffffff');
IF COL_LENGTH(N'dbo.Stores', N'ThemeTextColor') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeTextColor CHAR(7) NOT NULL CONSTRAINT DF_Stores_ThemeTextColor DEFAULT('#232b49');
IF COL_LENGTH(N'dbo.Stores', N'ThemeFontCode') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeFontCode NVARCHAR(20) NOT NULL CONSTRAINT DF_Stores_ThemeFontCode DEFAULT(N'iran-yekan');
IF COL_LENGTH(N'dbo.Stores', N'ThemeCornerStyle') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeCornerStyle NVARCHAR(20) NOT NULL CONSTRAINT DF_Stores_ThemeCornerStyle DEFAULT(N'soft');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemeFontCode')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeFontCode CHECK(ThemeFontCode IN (N'iran-yekan',N'system',N'serif',N'modern'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemeCornerStyle')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeCornerStyle CHECK(ThemeCornerStyle IN (N'soft',N'square',N'round'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemePrimaryColor')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemePrimaryColor CHECK(LEN(ThemePrimaryColor)=7 AND ThemePrimaryColor LIKE '#[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemeSecondaryColor')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeSecondaryColor CHECK(LEN(ThemeSecondaryColor)=7 AND ThemeSecondaryColor LIKE '#[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemeBackgroundColor')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeBackgroundColor CHECK(LEN(ThemeBackgroundColor)=7 AND ThemeBackgroundColor LIKE '#[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Stores_ThemeTextColor')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeTextColor CHECK(LEN(ThemeTextColor)=7 AND ThemeTextColor LIKE '#[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]');

COMMIT TRANSACTION;

SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.Stores', N'ThemeCode') IS NULL
    ALTER TABLE dbo.Stores ADD ThemeCode NVARCHAR(20) NOT NULL CONSTRAINT DF_Stores_ThemeCode DEFAULT(N'classic');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Stores_ThemeCode')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeCode CHECK(ThemeCode IN (N'classic',N'minimal',N'vibrant'));
COMMIT TRANSACTION;

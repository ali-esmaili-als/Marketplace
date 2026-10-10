SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Stores', N'PaletteCode') IS NULL
    ALTER TABLE dbo.Stores ADD PaletteCode NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Stores_PaletteCode DEFAULT(N'ocean');

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Stores_ThemeCode')
    ALTER TABLE dbo.Stores DROP CONSTRAINT CK_Stores_ThemeCode;

ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_ThemeCode CHECK
(
    ThemeCode IN (N'classic',N'minimal',N'vibrant',N'editorial',N'boutique',N'magazine',N'grid',N'luxe',N'organic',N'tech',N'fashion',N'gallery',N'market',N'mono',N'pastel',N'bold',N'nordic',N'artisan',N'urban',N'elegant')
);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Stores_PaletteCode')
    ALTER TABLE dbo.Stores ADD CONSTRAINT CK_Stores_PaletteCode CHECK
    (
        PaletteCode IN (N'ocean',N'forest',N'sunset',N'rose',N'monochrome')
    );

COMMIT TRANSACTION;

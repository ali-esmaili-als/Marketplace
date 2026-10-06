IF COL_LENGTH(N'dbo.Sellers', N'MaxStoreCount') IS NULL
BEGIN
    ALTER TABLE dbo.Sellers
        ADD MaxStoreCount INT NOT NULL
            CONSTRAINT DF_Sellers_MaxStoreCount DEFAULT (1);
END;
GO

-- Existing sellers keep the safe default of one store.
-- Admin can later raise MaxStoreCount for VIP sellers.
GO
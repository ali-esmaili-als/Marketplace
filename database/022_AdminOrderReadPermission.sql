/*
 022_AdminOrderReadPermission.sql
 Adds a dedicated read-only permission for the admin order operations workspace.
 Safe to run more than once. Does not modify commerce or financial data.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Rules WHERE Code = N'Admin.Order.Read')
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.Rules WHERE Id = 3008 AND Code <> N'Admin.Order.Read')
        THROW 51022, 'Rule ID 3008 is already used by another permission.', 1;

    INSERT dbo.Rules (Id, Code, Name, ActionType, IsActive)
    VALUES (3008, N'Admin.Order.Read', N'Read and investigate marketplace orders', 3, 1);
END
ELSE
BEGIN
    UPDATE dbo.Rules
    SET Name = N'Read and investigate marketplace orders', ActionType = 3, IsActive = 1
    WHERE Code = N'Admin.Order.Read';
END;

COMMIT TRANSACTION;

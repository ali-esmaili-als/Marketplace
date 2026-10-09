/*
 011 - Enforce one active complaint per order on an existing Marketplace database.
 Run after the earlier database patches and before deploying application code that
 relies on this invariant. For a new empty database, Marketplace_Complete.sql
 already creates the same index.

 Open=1 and UnderReview=2 are the only active complaint states. Resolved/cancelled
 complaints do not occupy the active slot.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Complaints', N'U') IS NULL
    THROW 51101, 'Cannot apply patch 011: dbo.Complaints does not exist. Apply the Marketplace schema first.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Complaints
    WHERE Status IN (1, 2)
    GROUP BY OrderId
    HAVING COUNT_BIG(*) > 1
)
    THROW 51102, 'Cannot apply patch 011: duplicate active complaints exist for at least one order. Review and resolve them before retrying.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Complaints')
      AND name = N'UX_Complaints_OneActivePerOrder'
)
    CREATE UNIQUE INDEX UX_Complaints_OneActivePerOrder
        ON dbo.Complaints(OrderId)
        WHERE Status IN (1, 2);

/*
    Read-only operational diagnostics for Marketplace financial state.
    Run against the Marketplace database during reconciliation or incident review.
    These queries report suspicious rows; they do not repair balances or change state.
*/

SET NOCOUNT ON;

PRINT '1. Negative seller balance buckets (should return no rows)';
SELECT Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR,
       ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc
FROM dbo.SellerBalances
WHERE AvailableIRR < 0
   OR PendingIRR < 0
   OR BlockedIRR < 0
   OR ReservedForSettlementIRR < 0
   OR LiabilityIRR < 0;

PRINT '2. Completed settlements without a matching FINAL settlement ledger entry (Available bucket)';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       s.Reference, s.CompletedAtUtc
FROM dbo.Settlements AS s
WHERE s.Status = 3
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.SettlementId = s.Id
        AND bt.Type = 4 -- Settlement
        AND bt.Bucket = 1 -- Available; request-time reservation uses Bucket=4
        AND bt.AmountIRR = s.AmountIRR
  );

PRINT '3. Failed settlements without a matching failed-settlement ledger entry';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       s.FailureReason, s.CompletedAtUtc
FROM dbo.Settlements AS s
WHERE s.Status = 4
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.SettlementId = s.Id
        AND bt.Type = 14 -- SettlementFailed
  );

PRINT '4. Processing/on-hold settlements that already have a final settlement ledger entry';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       bt.Id AS LedgerTransactionId, bt.Type AS LedgerType, bt.CreatedAtUtc
FROM dbo.Settlements AS s
JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE s.Status IN (2, 6) -- Processing, OnHold
  AND bt.Type IN (4, 14); -- Settlement, SettlementFailed

PRINT '5. Completed refunds without a refund ledger entry';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       r.Status, r.ProviderReference, r.CompletedAtUtc
FROM dbo.Refunds AS r
WHERE r.Status = 4
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = r.OrderId
        AND bt.Type = 3 -- Refund
  );

PRINT '6. Duplicate sale ledger entries per order (should return no rows)';
SELECT bt.OrderId, COUNT_BIG(*) AS SaleLedgerCount
FROM dbo.BalanceTransactions AS bt
WHERE bt.OrderId IS NOT NULL
  AND bt.Type = 1 -- Sale
GROUP BY bt.OrderId
HAVING COUNT_BIG(*) > 1;

PRINT '7. Active refunds per order (at most one should exist)';
SELECT r.OrderId, COUNT_BIG(*) AS ActiveRefundCount
FROM dbo.Refunds AS r
WHERE r.Status < 4 -- Requested, Approved, Processing
GROUP BY r.OrderId
HAVING COUNT_BIG(*) > 1;

PRINT '8. Financial records for manual review: ambiguous settlements and refunds';
SELECT 'Settlement' AS RecordType, s.Id AS RecordId, s.SellerId,
       s.AmountIRR, s.Status, s.RequestedAtUtc AS CreatedAtUtc
FROM dbo.Settlements AS s
WHERE s.Status IN (2, 6) -- Processing, OnHold
UNION ALL
SELECT 'Refund', r.Id, o.SellerId, r.AmountIRR, r.Status, r.RequestedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE r.Status = 3 -- Processing
ORDER BY CreatedAtUtc, RecordType, RecordId;


PRINT '9. Seller reserved-for-settlement bucket differs from unsettled settlement totals';
;WITH ActiveSettlementTotals AS
(
    SELECT SellerId, SUM(AmountIRR) AS ExpectedReservedIRR,
           COUNT_BIG(*) AS ActiveSettlementCount
    FROM dbo.Settlements
    WHERE Status IN (1, 2, 6) -- Requested, Processing, OnHold
    GROUP BY SellerId
)
SELECT b.SellerId, b.ReservedForSettlementIRR,
       COALESCE(a.ExpectedReservedIRR, 0) AS ExpectedReservedIRR,
       b.ReservedForSettlementIRR - COALESCE(a.ExpectedReservedIRR, 0) AS DifferenceIRR,
       COALESCE(a.ActiveSettlementCount, 0) AS ActiveSettlementCount
FROM dbo.SellerBalances AS b
LEFT JOIN ActiveSettlementTotals AS a ON a.SellerId = b.SellerId
WHERE b.ReservedForSettlementIRR <> COALESCE(a.ExpectedReservedIRR, 0);

PRINT '10. More than one final settlement ledger entry for a settlement';
SELECT bt.SettlementId, COUNT_BIG(*) AS FinalLedgerCount,
       MIN(bt.AmountIRR) AS MinimumAmountIRR, MAX(bt.AmountIRR) AS MaximumAmountIRR
FROM dbo.BalanceTransactions AS bt
WHERE bt.SettlementId IS NOT NULL
  AND bt.Type = 4 -- Settlement
  AND bt.Bucket = 1 -- Available bucket means payout finalization, not reservation
GROUP BY bt.SettlementId
HAVING COUNT_BIG(*) > 1;

PRINT '11. Failed settlement final ledger amount differs from settlement amount';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR AS SettlementAmountIRR,
       bt.Id AS LedgerTransactionId, bt.AmountIRR AS LedgerAmountIRR,
       s.Status, s.FailureReason
FROM dbo.Settlements AS s
JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE s.Status = 4
  AND bt.Type = 14 -- SettlementFailed
  AND bt.AmountIRR <> s.AmountIRR;

PRINT '12. Settlements missing their original reservation ledger entry';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status, s.RequestedAtUtc
FROM dbo.Settlements AS s
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.BalanceTransactions AS bt
    WHERE bt.SettlementId = s.Id
      AND bt.Type = 4 -- Settlement
      AND bt.Bucket = 4 -- ReservedForSettlement
      AND bt.AmountIRR = s.AmountIRR
);

PRINT '13. Settlements with a final ledger entry but a non-terminal status';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       bt.Id AS LedgerTransactionId, bt.Type AS LedgerType, bt.Bucket,
       bt.AmountIRR AS LedgerAmountIRR, bt.CreatedAtUtc
FROM dbo.Settlements AS s
JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE s.Status IN (1, 2, 6) -- Requested, Processing, OnHold
  AND ((bt.Type = 4 AND bt.Bucket = 1) OR bt.Type = 14);

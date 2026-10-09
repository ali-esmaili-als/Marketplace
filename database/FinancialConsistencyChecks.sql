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

PRINT '2. Completed settlements without a matching settlement ledger entry';
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

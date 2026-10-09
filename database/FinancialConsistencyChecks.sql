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

PRINT '3. Failed settlements without an exact matching failed-settlement ledger entry';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       s.FailureReason, s.CompletedAtUtc
FROM dbo.Settlements AS s
WHERE s.Status = 4
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.SettlementId = s.Id
        AND bt.SellerId = s.SellerId
        AND bt.Type = 14 -- SettlementFailed
        AND bt.Bucket = 4 -- ReservedForSettlement
        AND bt.AmountIRR = s.AmountIRR
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
SELECT COALESCE(b.SellerId, a.SellerId) AS SellerId,
       b.ReservedForSettlementIRR,
       COALESCE(a.ExpectedReservedIRR, 0) AS ExpectedReservedIRR,
       CASE WHEN b.SellerId IS NULL THEN NULL
            ELSE b.ReservedForSettlementIRR - COALESCE(a.ExpectedReservedIRR, 0) END AS DifferenceIRR,
       COALESCE(a.ActiveSettlementCount, 0) AS ActiveSettlementCount,
       CASE WHEN b.SellerId IS NULL THEN 'MISSING_SELLER_BALANCE'
            WHEN b.ReservedForSettlementIRR <> COALESCE(a.ExpectedReservedIRR, 0) THEN 'RESERVED_TOTAL_MISMATCH'
            ELSE 'OK' END AS Finding
FROM dbo.SellerBalances AS b
FULL OUTER JOIN ActiveSettlementTotals AS a ON a.SellerId = b.SellerId
WHERE b.SellerId IS NULL
   OR b.ReservedForSettlementIRR <> COALESCE(a.ExpectedReservedIRR, 0);

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


PRINT '14. Settlement ledger entries linked to a different seller than the settlement';
SELECT s.Id AS SettlementId, s.SellerId AS SettlementSellerId,
       bt.Id AS LedgerTransactionId, bt.SellerId AS LedgerSellerId,
       s.AmountIRR AS SettlementAmountIRR, bt.AmountIRR AS LedgerAmountIRR,
       bt.Type AS LedgerType, bt.Bucket, bt.CreatedAtUtc
FROM dbo.Settlements AS s
JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE bt.SellerId <> s.SellerId;


PRINT '15. Duplicate original settlement reservation ledger entries';
SELECT bt.SettlementId, COUNT_BIG(*) AS ReservationLedgerCount,
       MIN(bt.AmountIRR) AS MinimumAmountIRR, MAX(bt.AmountIRR) AS MaximumAmountIRR
FROM dbo.BalanceTransactions AS bt
WHERE bt.SettlementId IS NOT NULL
  AND bt.Type = 4 -- Settlement
  AND bt.Bucket = 4 -- ReservedForSettlement
GROUP BY bt.SettlementId
HAVING COUNT_BIG(*) > 1;

PRINT '16. Terminal settlement status conflicts with the opposite final ledger outcome';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       bt.Id AS LedgerTransactionId, bt.Type AS LedgerType, bt.Bucket,
       bt.AmountIRR AS LedgerAmountIRR, bt.CreatedAtUtc
FROM dbo.Settlements AS s
JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE (s.Status = 3 AND bt.Type = 14) -- Completed but marked failed in ledger
   OR (s.Status = 4 AND bt.Type = 4 AND bt.Bucket = 1); -- Failed but marked paid in ledger

PRINT '17. Completed refunds without a matching refund ledger entry for the same order and seller'; 
SELECT r.Id AS RefundId, r.OrderId, o.SellerId, r.AmountIRR, r.Status,
       r.ProviderReference, r.CompletedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE r.Status = 4
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = r.OrderId
        AND bt.SellerId = o.SellerId
        AND bt.Type = 3 -- Refund
  );

PRINT '18. Orders with multiple refund ledger entries (manual review required)';
SELECT bt.OrderId, COUNT_BIG(*) AS RefundLedgerCount,
       SUM(bt.AmountIRR) AS TotalRefundLedgerAmountIRR
FROM dbo.BalanceTransactions AS bt
WHERE bt.OrderId IS NOT NULL
  AND bt.Type = 3 -- Refund
GROUP BY bt.OrderId
HAVING COUNT_BIG(*) > 1;

PRINT '19. Refund ledger entries whose seller does not match the order seller';
SELECT bt.Id AS LedgerTransactionId, bt.OrderId,
       bt.SellerId AS LedgerSellerId, o.SellerId AS OrderSellerId,
       bt.AmountIRR, bt.Bucket, bt.CreatedAtUtc
FROM dbo.BalanceTransactions AS bt
JOIN dbo.Orders AS o ON o.Id = bt.OrderId
WHERE bt.Type = 3
  AND bt.SellerId <> o.SellerId;

PRINT '20. Completed refunds whose order or payment is not marked refunded';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       o.Status AS OrderStatus, p.Status AS PaymentStatus,
       r.ProviderReference, r.CompletedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
JOIN dbo.Payments AS p ON p.Id = r.PaymentId
WHERE r.Status = 4
  AND (o.Status <> 8 OR p.Status <> 6); -- Order Refunded, Payment Refunded

PRINT '21. Processing refunds whose order or payment has already reached a terminal state';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       r.Status AS RefundStatus, o.Status AS OrderStatus, p.Status AS PaymentStatus
FROM dbo.Refunds AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
JOIN dbo.Payments AS p ON p.Id = r.PaymentId
WHERE r.Status = 3
  AND (o.Status = 8 OR p.Status = 6); -- A processing refund should not already be finalized

PRINT '22. Commission reversals whose order/refund/commission linkage is inconsistent';
SELECT cr.Id AS CommissionReversalId, cr.OrderId, cr.RefundId,
       cr.CommissionId, c.OrderId AS CommissionOrderId,
       r.OrderId AS RefundOrderId, cr.RefundAmountIRR,
       cr.ReversedCommissionIRR, c.CommissionAmountIRR
FROM dbo.CommissionReversals AS cr
JOIN dbo.Commissions AS c ON c.Id = cr.CommissionId
JOIN dbo.Refunds AS r ON r.Id = cr.RefundId
WHERE cr.OrderId <> c.OrderId
   OR cr.OrderId <> r.OrderId
   OR cr.ReversedCommissionIRR > c.CommissionAmountIRR;

PRINT '23. Refunds with more than one reconciliation audit entry (review repeated manual actions)';
SELECT a.RefundId, COUNT_BIG(*) AS AuditCount,
       MIN(a.CreatedAtUtc) AS FirstAuditAtUtc,
       MAX(a.CreatedAtUtc) AS LastAuditAtUtc
FROM dbo.RefundReconciliationAudits AS a
GROUP BY a.RefundId
HAVING COUNT_BIG(*) > 1;

PRINT '24. Completed refund totals exceed the captured payment amount';
;WITH CompletedRefundTotals AS
(
    SELECT PaymentId, SUM(AmountIRR) AS CompletedRefundAmountIRR,
           COUNT_BIG(*) AS CompletedRefundCount
    FROM dbo.Refunds
    WHERE Status = 4 -- Completed
    GROUP BY PaymentId
)
SELECT p.Id AS PaymentId, p.OrderId, p.AmountIRR AS CapturedPaymentAmountIRR,
       r.CompletedRefundAmountIRR, r.CompletedRefundCount,
       r.CompletedRefundAmountIRR - p.AmountIRR AS ExcessRefundAmountIRR
FROM CompletedRefundTotals AS r
JOIN dbo.Payments AS p ON p.Id = r.PaymentId
WHERE r.CompletedRefundAmountIRR > p.AmountIRR;

PRINT '25. Latest manual reconciliation outcome conflicts with terminal refund status';
;WITH LatestRefundAudit AS
(
    SELECT a.RefundId, a.AdminUserId, a.TransferCompleted, a.BankReference,
           a.Note, a.CreatedAtUtc, a.Id,
           ROW_NUMBER() OVER
           (
               PARTITION BY a.RefundId
               ORDER BY a.CreatedAtUtc DESC, a.Id DESC
           ) AS rn
    FROM dbo.RefundReconciliationAudits AS a
)
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.Status AS RefundStatus,
       a.AdminUserId, a.TransferCompleted AS LatestAuditTransferCompleted,
       a.BankReference, a.Note, a.CreatedAtUtc AS AuditCreatedAtUtc
FROM dbo.Refunds AS r
JOIN LatestRefundAudit AS a ON a.RefundId = r.Id AND a.rn = 1
WHERE (r.Status = 4 AND a.TransferCompleted = 0) -- Completed but last audit says not sent
   OR (r.Status = 5 AND a.TransferCompleted = 1); -- Failed but last audit says sent

PRINT '26. Total commission reversals exceed the original commission amount';
;WITH ReversedCommissionTotals AS
(
    SELECT CommissionId, SUM(ReversedCommissionIRR) AS TotalReversedCommissionIRR,
           COUNT_BIG(*) AS ReversalCount
    FROM dbo.CommissionReversals
    GROUP BY CommissionId
)
SELECT c.Id AS CommissionId, c.OrderId, c.CommissionAmountIRR,
       r.TotalReversedCommissionIRR, r.ReversalCount
FROM ReversedCommissionTotals AS r
JOIN dbo.Commissions AS c ON c.Id = r.CommissionId
WHERE r.TotalReversedCommissionIRR > c.CommissionAmountIRR;

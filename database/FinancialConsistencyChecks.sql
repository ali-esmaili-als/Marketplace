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
      WHERE bt.Type = 3 -- Refund
        AND (bt.RefundId = r.Id
             OR (bt.RefundId IS NULL AND bt.OrderId = r.OrderId))
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
      WHERE bt.Type = 3 -- Refund
        AND (bt.RefundId = r.Id
             OR (bt.RefundId IS NULL AND bt.OrderId = r.OrderId AND bt.SellerId = o.SellerId))
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

PRINT '27. Payment amount differs from its order total';
SELECT p.Id AS PaymentId, p.OrderId,
       p.AmountIRR AS PaymentAmountIRR, o.TotalAmountIRR AS OrderTotalAmountIRR,
       p.Status AS PaymentStatus, o.Status AS OrderStatus
FROM dbo.Payments AS p
JOIN dbo.Orders AS o ON o.Id = p.OrderId
WHERE p.AmountIRR <> o.TotalAmountIRR;

PRINT '28. Captured/refunded payment has no corresponding sale ledger entry';
SELECT p.Id AS PaymentId, p.OrderId, p.AmountIRR, p.Status AS PaymentStatus,
       o.Status AS OrderStatus, o.SellerId, o.SellerAmountIRR
FROM dbo.Payments AS p
JOIN dbo.Orders AS o ON o.Id = p.OrderId
WHERE p.Status IN (3, 6, 7) -- Succeeded, Refunded, PartiallyRefunded
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = o.Id
        AND bt.SellerId = o.SellerId
        AND bt.Type = 1 -- Sale
        AND bt.AmountIRR = o.SellerAmountIRR
  );

PRINT '29. Sale ledger amount or seller does not match the order snapshot';
SELECT bt.Id AS LedgerTransactionId, bt.OrderId,
       bt.SellerId AS LedgerSellerId, o.SellerId AS OrderSellerId,
       bt.AmountIRR AS LedgerAmountIRR, o.SellerAmountIRR AS ExpectedSellerAmountIRR,
       bt.Bucket, bt.CreatedAtUtc
FROM dbo.BalanceTransactions AS bt
JOIN dbo.Orders AS o ON o.Id = bt.OrderId
WHERE bt.Type = 1 -- Sale
  AND (bt.SellerId <> o.SellerId OR bt.AmountIRR <> o.SellerAmountIRR);

PRINT '30. Orders with multiple seller balance holds';
SELECT h.OrderId, COUNT_BIG(*) AS HoldCount,
       COUNT(DISTINCT h.SellerId) AS SellerCount,
       MIN(h.AmountIRR) AS MinimumHoldAmountIRR,
       MAX(h.AmountIRR) AS MaximumHoldAmountIRR
FROM dbo.SellerBalanceHolds AS h
WHERE h.OrderId IS NOT NULL
GROUP BY h.OrderId
HAVING COUNT_BIG(*) > 1;

PRINT '31. Paid-through-terminal orders without a delivery record';
SELECT o.Id AS OrderId, o.SellerId, o.Status AS OrderStatus,
       o.PaidAtUtc, o.DeliveryExpiresAtUtc, o.TotalAmountIRR
FROM dbo.Orders AS o
WHERE o.Status IN (2, 3, 4, 5, 6, 7, 8, 9)
  AND NOT EXISTS (SELECT 1 FROM dbo.Deliveries AS d WHERE d.OrderId = o.Id);

PRINT '32. Delivered orders whose delivery record is missing or not delivered';
SELECT o.Id AS OrderId, o.SellerId, o.Status AS OrderStatus,
       d.Id AS DeliveryId, d.Status AS DeliveryStatus,
       o.DeliveredAtUtc, d.DeliveredAtUtc AS DeliveryDeliveredAtUtc
FROM dbo.Orders AS o
LEFT JOIN dbo.Deliveries AS d ON d.OrderId = o.Id
WHERE o.Status IN (5, 9) -- Delivered or Completed
  AND (d.Id IS NULL OR d.Status <> 3);

PRINT '33. Delivery marked delivered while order state does not reflect delivery';
SELECT d.Id AS DeliveryId, d.OrderId, d.SellerId,
       d.Status AS DeliveryStatus, o.Status AS OrderStatus,
       d.DeliveredAtUtc, d.ConfirmationReference
FROM dbo.Deliveries AS d
JOIN dbo.Orders AS o ON o.Id = d.OrderId
WHERE d.Status = 3
  AND o.Status NOT IN (5, 7, 8, 9); -- Delivered, RefundRequested, Refunded, Completed

PRINT '34. Active inventory reservations attached to terminal order states';
SELECT r.Id AS ReservationId, r.OrderId, r.ProductVariantId,
       r.Quantity, r.Status AS ReservationStatus, r.ExpiresAtUtc,
       o.Status AS OrderStatus
FROM dbo.InventoryReservations AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE r.Status = 1 -- Active
  AND o.Status IN (5, 6, 7, 8, 9, 10); -- Delivered, DeliveryExpired, RefundRequested, Refunded, Completed, Cancelled

PRINT '35. Inventory reserved quantity differs from active reservation totals';
;WITH ActiveReservationTotals AS
(
    SELECT ProductVariantId, SUM(Quantity) AS ExpectedReservedQuantity,
           COUNT_BIG(*) AS ActiveReservationCount
    FROM dbo.InventoryReservations
    WHERE Status = 1 -- Active
    GROUP BY ProductVariantId
)
SELECT i.Id AS InventoryItemId, i.ProductVariantId,
       i.StockQuantity, i.ReservedQuantity,
       COALESCE(r.ExpectedReservedQuantity, 0) AS ExpectedReservedQuantity,
       i.ReservedQuantity - COALESCE(r.ExpectedReservedQuantity, 0) AS DifferenceQuantity,
       COALESCE(r.ActiveReservationCount, 0) AS ActiveReservationCount
FROM dbo.InventoryItems AS i
LEFT JOIN ActiveReservationTotals AS r ON r.ProductVariantId = i.ProductVariantId
WHERE i.ReservedQuantity <> COALESCE(r.ExpectedReservedQuantity, 0);

PRINT '36. Delivery-code use timestamp conflicts with delivery completion';
SELECT c.Id AS DeliveryCodeId, c.OrderId,
       c.IssuedAtUtc, c.ExpiresAtUtc, c.UsedAtUtc,
       d.Status AS DeliveryStatus, d.DeliveredAtUtc,
       c.FailedAttempts
FROM dbo.DeliveryCodes AS c
LEFT JOIN dbo.Deliveries AS d ON d.OrderId = c.OrderId
WHERE (c.UsedAtUtc IS NOT NULL AND (d.Id IS NULL OR d.Status <> 3))
   OR (d.Status = 3 AND c.UsedAtUtc IS NULL);

PRINT '37. Complaints whose customer or seller does not match the order';
SELECT c.Id AS ComplaintId, c.OrderId,
       c.CustomerId AS ComplaintCustomerId, o.CustomerId AS OrderCustomerId,
       c.SellerId AS ComplaintSellerId, o.SellerId AS OrderSellerId,
       c.Status AS ComplaintStatus, o.Status AS OrderStatus
FROM dbo.Complaints AS c
JOIN dbo.Orders AS o ON o.Id = c.OrderId
WHERE c.CustomerId <> o.CustomerId
   OR c.SellerId <> o.SellerId;

PRINT '38. Active complaints attached to orders outside the delivered state';
SELECT c.Id AS ComplaintId, c.OrderId, c.CustomerId, c.SellerId,
       c.Status AS ComplaintStatus, c.CreatedAtUtc,
       o.Status AS OrderStatus, o.ComplaintExpiresAtUtc
FROM dbo.Complaints AS c
JOIN dbo.Orders AS o ON o.Id = c.OrderId
WHERE c.Status IN (1, 2) -- Open, UnderReview
  AND o.Status <> 5; -- Delivered; refund/close must follow complaint resolution

PRINT '39. Customer-won complaints without an order awaiting or completing refund';
SELECT c.Id AS ComplaintId, c.OrderId, c.Status AS ComplaintStatus,
       c.ResolvedAtUtc, o.Status AS OrderStatus
FROM dbo.Complaints AS c
JOIN dbo.Orders AS o ON o.Id = c.OrderId
WHERE c.Status = 3 -- CustomerWon
  AND o.Status NOT IN (7, 8); -- RefundRequested or Refunded

PRINT '40. Seller-won complaints without completed order or released seller hold';
SELECT c.Id AS ComplaintId, c.OrderId, c.Status AS ComplaintStatus,
       o.Status AS OrderStatus, h.Id AS HoldId, h.Status AS HoldStatus,
       h.AmountIRR AS HoldAmountIRR, o.SellerAmountIRR
FROM dbo.Complaints AS c
JOIN dbo.Orders AS o ON o.Id = c.OrderId
LEFT JOIN dbo.SellerBalanceHolds AS h ON h.OrderId = o.Id
WHERE c.Status = 4 -- SellerWon
  AND (o.Status <> 9 OR h.Id IS NULL OR h.Status <> 2); -- Completed, Released

PRINT '41. Active seller holds attached to terminal orders';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       h.Status AS HoldStatus, o.Status AS OrderStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE h.Status = 1 -- Active
  AND o.Status IN (8, 9, 10); -- Refunded, Completed, Cancelled

PRINT '42. Terminal seller hold state conflicts with order outcome';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       h.Status AS HoldStatus, o.Status AS OrderStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE (h.Status = 2 AND o.Status NOT IN (9)) -- Released should mean Completed
   OR (h.Status = 3 AND o.Status <> 8); -- Consumed should mean Refunded

PRINT '43. Orders with multiple active complaints';
SELECT c.OrderId, COUNT_BIG(*) AS ActiveComplaintCount,
       MIN(c.CreatedAtUtc) AS FirstComplaintAtUtc,
       MAX(c.CreatedAtUtc) AS LastComplaintAtUtc
FROM dbo.Complaints AS c
WHERE c.Status IN (1, 2) -- Open, UnderReview
GROUP BY c.OrderId
HAVING COUNT_BIG(*) > 1;

PRINT '44. Financially progressed orders without a seller hold';
SELECT o.Id AS OrderId, o.SellerId, o.SellerAmountIRR,
       o.Status AS OrderStatus, o.PaidAtUtc, o.DeliveredAtUtc
FROM dbo.Orders AS o
WHERE o.Status IN (2, 3, 4, 5, 6, 7, 8, 9) -- Paid through terminal financial states
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SellerBalanceHolds AS h
      WHERE h.OrderId = o.Id
        AND h.SellerId = o.SellerId
        AND h.AmountIRR = o.SellerAmountIRR
  );



PRINT '45. Delivered-or-later orders with an active hold but no matching complaint-hold ledger entry';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       o.Status AS OrderStatus, h.Status AS HoldStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE h.Status = 1
  AND o.Status IN (5, 7, 8, 9) -- Delivered, RefundRequested, Refunded, Completed
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = h.OrderId
        AND bt.SellerId = h.SellerId
        AND bt.Type = 10 -- ComplaintHold
        AND bt.Bucket = 3 -- Blocked
        AND bt.AmountIRR = h.AmountIRR
  );

PRINT '46. Released seller holds missing their matching blocked-balance release ledger';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       h.Status AS HoldStatus, h.CompletedAtUtc, o.Status AS OrderStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE h.Status = 2 -- Released
  AND o.Status = 9 -- Completed
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = h.OrderId
        AND bt.SellerId = h.SellerId
        AND bt.Type = 11 -- ComplaintHoldReleased
        AND bt.Bucket = 3 -- Blocked
        AND bt.AmountIRR = h.AmountIRR
  );

PRINT '47. Consumed seller holds missing their matching blocked-balance consumption ledger';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       h.Status AS HoldStatus, h.CompletedAtUtc, o.Status AS OrderStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE h.Status = 3 -- Consumed
  AND o.Status = 8 -- Refunded
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.OrderId = h.OrderId
        AND bt.SellerId = h.SellerId
        AND bt.Type = 12 -- ComplaintHoldConsumed
        AND bt.Bucket = 3 -- Blocked
        AND bt.AmountIRR = h.AmountIRR
  );

PRINT '48. Complaint resolution state missing or contradicting its resolution evidence';
SELECT c.Id AS ComplaintId, c.OrderId, c.Status AS ComplaintStatus,
       c.ResolutionNote, c.ResolvedAtUtc, c.CreatedAtUtc
FROM dbo.Complaints AS c
WHERE (c.Status IN (3, 4) AND
       (LEN(LTRIM(RTRIM(ISNULL(c.ResolutionNote, N'')))) = 0 OR c.ResolvedAtUtc IS NULL))
   OR (c.Status IN (1, 2) AND
       (c.ResolutionNote IS NOT NULL OR c.ResolvedAtUtc IS NOT NULL));


PRINT '49. Seller balance bucket differs from its latest ledger snapshot';
;WITH CurrentBuckets AS
(
    SELECT sb.SellerId, v.Bucket, v.BalanceIRR
    FROM dbo.SellerBalances AS sb
    CROSS APPLY (VALUES
        (1, sb.AvailableIRR),
        (2, sb.PendingIRR),
        (3, sb.BlockedIRR),
        (4, sb.ReservedForSettlementIRR),
        (5, sb.LiabilityIRR)
    ) AS v(Bucket, BalanceIRR)
)
SELECT cb.SellerId, cb.Bucket, cb.BalanceIRR AS CurrentBalanceIRR,
       latest.BalanceAfterIRR AS LatestLedgerBalanceIRR,
       latest.TransactionId, latest.TransactionType, latest.CreatedAtUtc
FROM CurrentBuckets AS cb
OUTER APPLY
(
    SELECT TOP (1) bt.Id AS TransactionId, bt.Type AS TransactionType,
           bt.BalanceAfterIRR, bt.CreatedAtUtc
    FROM dbo.BalanceTransactions AS bt
    WHERE bt.SellerId = cb.SellerId AND bt.Bucket = cb.Bucket
    ORDER BY bt.CreatedAtUtc DESC, bt.Id DESC
) AS latest
WHERE (latest.TransactionId IS NULL AND cb.BalanceIRR <> 0)
   OR (latest.TransactionId IS NOT NULL AND latest.BalanceAfterIRR <> cb.BalanceIRR);

PRINT '50. Balance transactions reference an order owned by a different seller';
SELECT bt.Id AS BalanceTransactionId, bt.SellerId AS LedgerSellerId,
       bt.OrderId, o.SellerId AS OrderSellerId, bt.Type, bt.Bucket, bt.AmountIRR
FROM dbo.BalanceTransactions AS bt
JOIN dbo.Orders AS o ON o.Id = bt.OrderId
WHERE bt.OrderId IS NOT NULL
  AND bt.SellerId <> o.SellerId;

PRINT '51. Balance transactions reference a settlement owned by a different seller';
SELECT bt.Id AS BalanceTransactionId, bt.SellerId AS LedgerSellerId,
       bt.SettlementId, s.SellerId AS SettlementSellerId,
       bt.Type, bt.Bucket, bt.AmountIRR
FROM dbo.BalanceTransactions AS bt
JOIN dbo.Settlements AS s ON s.Id = bt.SettlementId
WHERE bt.SettlementId IS NOT NULL
  AND bt.SellerId <> s.SellerId;

PRINT '52. Multiple sale ledger entries exist for the same order';
SELECT bt.OrderId, COUNT_BIG(*) AS SaleLedgerCount,
       MIN(bt.CreatedAtUtc) AS FirstSaleLedgerAtUtc,
       MAX(bt.CreatedAtUtc) AS LastSaleLedgerAtUtc
FROM dbo.BalanceTransactions AS bt
WHERE bt.Type = 1 -- Sale
  AND bt.OrderId IS NOT NULL
GROUP BY bt.OrderId
HAVING COUNT_BIG(*) > 1;


PRINT '53. Duplicate non-null provider/authority pairs in payment transactions';
SELECT pt.Provider, pt.Authority, COUNT_BIG(*) AS DuplicateCount,
       MIN(pt.CreatedAtUtc) AS FirstSeenAtUtc, MAX(pt.CreatedAtUtc) AS LastSeenAtUtc
FROM dbo.PaymentTransactions AS pt
WHERE pt.Authority IS NOT NULL
GROUP BY pt.Provider, pt.Authority
HAVING COUNT_BIG(*) > 1;

PRINT '54. Multiple active refund attempts exist for the same order';
SELECT r.OrderId, COUNT_BIG(*) AS ActiveRefundCount,
       MIN(r.RequestedAtUtc) AS FirstRequestedAtUtc,
       MAX(r.RequestedAtUtc) AS LastRequestedAtUtc
FROM dbo.Refunds AS r
WHERE r.Status < 4 -- Requested, Approved, Processing
GROUP BY r.OrderId
HAVING COUNT_BIG(*) > 1;


PRINT '55. Successful payment transactions conflict with terminal payment status';
SELECT pt.Id AS PaymentTransactionId, pt.PaymentId, pt.Status AS TransactionStatus,
       p.Status AS PaymentStatus, pt.Provider, pt.Authority, pt.Reference,
       pt.AmountIRR AS TransactionAmountIRR, p.AmountIRR AS PaymentAmountIRR
FROM dbo.PaymentTransactions AS pt
JOIN dbo.Payments AS p ON p.Id = pt.PaymentId
WHERE pt.Status = 2 -- Succeeded
  AND p.Status NOT IN (3, 8); -- Succeeded or ReconciliationRequired after a post-gateway finalization failure

PRINT '56. Succeeded payments have no successful provider transaction';
SELECT p.Id AS PaymentId, p.OrderId, p.AmountIRR, p.Status AS PaymentStatus,
       p.Provider, p.Authority, p.ReferenceNumber
FROM dbo.Payments AS p
WHERE p.Status = 3 -- Succeeded
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.PaymentTransactions AS pt
      WHERE pt.PaymentId = p.Id
        AND pt.Status = 2 -- Succeeded
  );


PRINT '57. Successful provider transaction amount differs from its payment amount';
SELECT pt.Id AS PaymentTransactionId, pt.PaymentId,
       pt.AmountIRR AS TransactionAmountIRR, p.AmountIRR AS PaymentAmountIRR,
       pt.Provider, pt.Authority, pt.Reference
FROM dbo.PaymentTransactions AS pt
JOIN dbo.Payments AS p ON p.Id = pt.PaymentId
WHERE pt.Status = 2 -- Succeeded
  AND pt.AmountIRR <> p.AmountIRR;

PRINT '58. Successful provider transaction has no bank reference';
SELECT pt.Id AS PaymentTransactionId, pt.PaymentId, pt.Provider,
       pt.Authority, pt.Status, pt.Reference
FROM dbo.PaymentTransactions AS pt
WHERE pt.Status = 2 -- Succeeded
  AND LEN(LTRIM(RTRIM(ISNULL(pt.Reference, N'')))) = 0;

PRINT '59. Succeeded payment lacks its final reference or paid timestamp';
SELECT p.Id AS PaymentId, p.OrderId, p.Status, p.ReferenceNumber, p.PaidAtUtc,
       p.AmountIRR, p.Provider, p.Authority
FROM dbo.Payments AS p
WHERE p.Status = 3 -- Succeeded
  AND (LEN(LTRIM(RTRIM(ISNULL(p.ReferenceNumber, N'')))) = 0 OR p.PaidAtUtc IS NULL);


PRINT '60. Financial ledger or active settlement exists for a seller without a SellerBalance row';
;WITH SellerFinancePresence AS
(
    SELECT bt.SellerId, COUNT_BIG(*) AS LedgerTransactionCount,
           CAST(0 AS bigint) AS ActiveSettlementCount
    FROM dbo.BalanceTransactions AS bt
    GROUP BY bt.SellerId
    UNION ALL
    SELECT s.SellerId, CAST(0 AS bigint), COUNT_BIG(*)
    FROM dbo.Settlements AS s
    WHERE s.Status IN (1, 2, 6) -- Requested, Processing, OnHold
    GROUP BY s.SellerId
),
SellerFinanceTotals AS
(
    SELECT SellerId, SUM(LedgerTransactionCount) AS LedgerTransactionCount,
           SUM(ActiveSettlementCount) AS ActiveSettlementCount
    FROM SellerFinancePresence
    GROUP BY SellerId
)
SELECT x.SellerId, x.LedgerTransactionCount, x.ActiveSettlementCount
FROM SellerFinanceTotals AS x
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.SellerBalances AS sb WHERE sb.SellerId = x.SellerId
);


PRINT '61. Commission split or order snapshot differs from the order';
SELECT c.Id AS CommissionId, c.OrderId, c.SellerId AS CommissionSellerId,
       o.SellerId AS OrderSellerId, c.OrderAmountIRR, o.TotalAmountIRR,
       c.CommissionAmountIRR, c.SellerAmountIRR,
       c.CommissionAmountIRR + c.SellerAmountIRR AS SplitTotalIRR
FROM dbo.Commissions AS c
JOIN dbo.Orders AS o ON o.Id = c.OrderId
WHERE c.SellerId <> o.SellerId
   OR c.OrderAmountIRR <> o.TotalAmountIRR
   OR c.SellerAmountIRR <> c.OrderAmountIRR - c.CommissionAmountIRR;

PRINT '62. Completed refunds without a commission reversal although the order has a commission';
SELECT r.Id AS RefundId, r.OrderId, r.AmountIRR AS RefundAmountIRR,
       c.Id AS CommissionId, c.CommissionAmountIRR, r.CompletedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Commissions AS c ON c.OrderId = r.OrderId
WHERE r.Status = 4 -- Completed
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.CommissionReversals AS cr WHERE cr.RefundId = r.Id
  );

PRINT '63. Commission reversals whose refund/order/commission links or amounts disagree';
SELECT cr.Id AS CommissionReversalId, cr.OrderId AS ReversalOrderId,
       r.OrderId AS RefundOrderId, c.OrderId AS CommissionOrderId,
       cr.RefundId, cr.CommissionId, cr.RefundAmountIRR,
       r.AmountIRR AS ActualRefundAmountIRR, cr.ReversedCommissionIRR,
       c.CommissionAmountIRR
FROM dbo.CommissionReversals AS cr
JOIN dbo.Refunds AS r ON r.Id = cr.RefundId
JOIN dbo.Commissions AS c ON c.Id = cr.CommissionId
WHERE cr.OrderId <> r.OrderId
   OR cr.OrderId <> c.OrderId
   OR cr.RefundAmountIRR <> r.AmountIRR
   OR cr.ReversedCommissionIRR > c.CommissionAmountIRR;


PRINT '64. Refund ledger identity points to a different order or seller';
SELECT bt.Id AS BalanceTransactionId, bt.RefundId, r.OrderId AS RefundOrderId,
       bt.OrderId AS LedgerOrderId, bt.SellerId AS LedgerSellerId,
       o.SellerId AS OrderSellerId, bt.AmountIRR, bt.Bucket, bt.CreatedAtUtc
FROM dbo.BalanceTransactions AS bt
JOIN dbo.Refunds AS r ON r.Id = bt.RefundId
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE bt.Type = 3
  AND (bt.OrderId <> r.OrderId OR bt.SellerId <> o.SellerId);

PRINT '65. Seller balance holds whose seller or amount differs from the order snapshot';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId AS HoldSellerId,
       o.SellerId AS OrderSellerId, h.AmountIRR AS HoldAmountIRR,
       o.SellerAmountIRR AS OrderSellerAmountIRR, h.Status AS HoldStatus
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE h.SellerId <> o.SellerId
   OR h.AmountIRR <> o.SellerAmountIRR;

PRINT '66. Financially progressed orders missing their seller balance hold';
SELECT o.Id AS OrderId, o.SellerId, o.Status AS OrderStatus,
       o.SellerAmountIRR, o.PaidAtUtc, o.DeliveredAtUtc
FROM dbo.Orders AS o
WHERE o.Status IN (2, 3, 4, 5, 6, 7, 8, 9) -- Paid through Completed/Refunded
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.SellerBalanceHolds AS h WHERE h.OrderId = o.Id
  );

PRINT '67. Consumed/released seller balance hold conflicts with final order state';
SELECT h.Id AS HoldId, h.OrderId, h.SellerId, h.AmountIRR,
       h.Status AS HoldStatus, o.Status AS OrderStatus,
       h.CreatedAtUtc
FROM dbo.SellerBalanceHolds AS h
JOIN dbo.Orders AS o ON o.Id = h.OrderId
WHERE (h.Status = 3 AND o.Status <> 8) -- Consumed should accompany a refunded order
   OR (h.Status = 2 AND o.Status <> 9); -- Released should accompany a completed order

PRINT '68. Multiple successful provider transactions for one payment';
SELECT pt.PaymentId, COUNT_BIG(*) AS SuccessfulTransactionCount,
       MIN(pt.AmountIRR) AS MinimumAmountIRR,
       MAX(pt.AmountIRR) AS MaximumAmountIRR,
       MIN(pt.CreatedAtUtc) AS FirstSuccessAtUtc,
       MAX(pt.CreatedAtUtc) AS LastSuccessAtUtc
FROM dbo.PaymentTransactions AS pt
WHERE pt.Status = 3 -- Succeeded
GROUP BY pt.PaymentId
HAVING COUNT_BIG(*) > 1;

PRINT '69. Active inventory reservations attached to orders that should no longer reserve stock';
SELECT r.Id AS ReservationId, r.OrderId, r.ProductVariantId, r.Quantity,
       r.Status AS ReservationStatus, r.ExpiresAtUtc,
       o.Status AS OrderStatus, o.CreatedAtUtc
FROM dbo.InventoryReservations AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE r.Status = 1 -- Active
  AND o.Status IN (5, 6, 7, 8, 9, 10); -- Delivered or any later/terminal lifecycle state

PRINT '70. Inventory reserved quantity differs from the sum of active reservation records';
;WITH ActiveReservations AS
(
    SELECT ProductVariantId, SUM(Quantity) AS ActiveReservedQuantity,
           COUNT_BIG(*) AS ActiveReservationCount
    FROM dbo.InventoryReservations
    WHERE Status = 1 -- Active
    GROUP BY ProductVariantId
)
SELECT i.Id AS InventoryItemId, i.ProductVariantId,
       i.StockQuantity, i.ReservedQuantity,
       ISNULL(r.ActiveReservedQuantity, 0) AS ReservationRecordsQuantity,
       ISNULL(r.ActiveReservationCount, 0) AS ActiveReservationCount,
       i.ReservedQuantity - ISNULL(r.ActiveReservedQuantity, 0) AS DifferenceQuantity
FROM dbo.InventoryItems AS i
LEFT JOIN ActiveReservations AS r ON r.ProductVariantId = i.ProductVariantId
WHERE i.ReservedQuantity <> ISNULL(r.ActiveReservedQuantity, 0);

PRINT '71. Expired pending-payment inventory reservations awaiting lifecycle cleanup';
SELECT r.Id AS ReservationId, r.OrderId, r.ProductVariantId, r.Quantity,
       r.ExpiresAtUtc, r.CreatedAtUtc, o.Status AS OrderStatus
FROM dbo.InventoryReservations AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
WHERE r.Status = 1 -- Active
  AND o.Status = 1 -- PendingPayment
  AND r.ExpiresAtUtc <= SYSUTCDATETIME();


PRINT '72. Reserved seller balance differs from active settlement requests';
;WITH ActiveSettlementTotals AS
(
    SELECT s.SellerId, SUM(s.AmountIRR) AS ActiveSettlementAmountIRR,
           COUNT_BIG(*) AS ActiveSettlementCount
    FROM dbo.Settlements AS s
    WHERE s.Status IN (1, 2, 6) -- Requested, Processing, OnHold
    GROUP BY s.SellerId
)
SELECT sb.SellerId, sb.ReservedForSettlementIRR,
       ISNULL(ast.ActiveSettlementAmountIRR, 0) AS ActiveSettlementAmountIRR,
       sb.ReservedForSettlementIRR - ISNULL(ast.ActiveSettlementAmountIRR, 0) AS DifferenceIRR,
       ISNULL(ast.ActiveSettlementCount, 0) AS ActiveSettlementCount
FROM dbo.SellerBalances AS sb
LEFT JOIN ActiveSettlementTotals AS ast ON ast.SellerId = sb.SellerId
WHERE sb.ReservedForSettlementIRR <> ISNULL(ast.ActiveSettlementAmountIRR, 0);

PRINT '73. Completed settlements missing a bank reference';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       s.Reference, s.CompletedAtUtc, s.RequestedAtUtc
FROM dbo.Settlements AS s
WHERE s.Status = 3 -- Completed
  AND LEN(LTRIM(RTRIM(ISNULL(s.Reference, N'')))) = 0;

PRINT '74. Completed or failed settlements missing their outcome ledger transaction';
SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
       COUNT(bt.Id) AS SettlementLinkedLedgerTransactionCount,
       MIN(bt.CreatedAtUtc) AS FirstLedgerAtUtc,
       MAX(bt.CreatedAtUtc) AS LastLedgerAtUtc
FROM dbo.Settlements AS s
LEFT JOIN dbo.BalanceTransactions AS bt ON bt.SettlementId = s.Id
WHERE s.Status IN (3, 4) -- Completed, Failed
GROUP BY s.Id, s.SellerId, s.AmountIRR, s.Status
HAVING COUNT(bt.Id) < 2; -- request reservation plus completion/failure outcome



PRINT '75. Completed refunds whose amount differs from the full-refund order/payment snapshot';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId,
       r.AmountIRR AS RefundAmountIRR,
       o.TotalAmountIRR AS OrderTotalAmountIRR,
       p.AmountIRR AS PaymentAmountIRR,
       r.ProviderReference, r.CompletedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Orders AS o ON o.Id = r.OrderId
JOIN dbo.Payments AS p ON p.Id = r.PaymentId
WHERE r.Status = 4 -- Completed
  AND (r.AmountIRR <> o.TotalAmountIRR OR r.AmountIRR <> p.AmountIRR);

PRINT '76. Completed refunds with a commission but no reversal linked to that exact refund';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       c.Id AS CommissionId, c.CommissionAmountIRR,
       r.ProviderReference, r.CompletedAtUtc
FROM dbo.Refunds AS r
JOIN dbo.Commissions AS c ON c.OrderId = r.OrderId
WHERE r.Status = 4 -- Completed
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.CommissionReversals AS cr
      WHERE cr.RefundId = r.Id
        AND cr.CommissionId = c.Id
        AND cr.OrderId = r.OrderId
  );

PRINT '77. Failed or rejected refunds with refund-linked financial postings';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       r.Status AS RefundStatus, bt.Id AS LedgerTransactionId,
       bt.Type AS LedgerType, bt.AmountIRR AS LedgerAmountIRR,
       cr.Id AS CommissionReversalId, cr.ReversedCommissionIRR
FROM dbo.Refunds AS r
LEFT JOIN dbo.BalanceTransactions AS bt
    ON bt.RefundId = r.Id AND bt.Type = 3 -- Refund
LEFT JOIN dbo.CommissionReversals AS cr ON cr.RefundId = r.Id
WHERE r.Status IN (5, 6) -- Failed, Rejected
  AND (bt.Id IS NOT NULL OR cr.Id IS NOT NULL);

PRINT '78. Payments marked refunded without a completed refund record';
SELECT p.Id AS PaymentId, p.OrderId, p.AmountIRR,
       p.Status AS PaymentStatus, o.Status AS OrderStatus,
       p.ReferenceNumber
FROM dbo.Payments AS p
JOIN dbo.Orders AS o ON o.Id = p.OrderId
WHERE p.Status = 6 -- Refunded
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Refunds AS r
      WHERE r.PaymentId = p.Id
        AND r.Status = 4 -- Completed
  );

PRINT '79. Refunds left processing for more than 30 minutes (manual reconciliation candidate)';
SELECT r.Id AS RefundId, r.OrderId, r.PaymentId, r.AmountIRR,
       r.Status, r.RequestedAtUtc,
       DATEDIFF(MINUTE, r.RequestedAtUtc, SYSUTCDATETIME()) AS ProcessingMinutes,
       r.ProviderReference
FROM dbo.Refunds AS r
WHERE r.Status = 3 -- Processing
  AND r.RequestedAtUtc < DATEADD(MINUTE, -30, SYSUTCDATETIME());

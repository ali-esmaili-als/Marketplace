/*
    Financial ledger / seller-balance reconciliation diagnostics
    READ-ONLY: this script intentionally contains no INSERT, UPDATE, DELETE, MERGE,
    DDL, or repair logic. Returned rows are investigation candidates, not repair instructions.

    Run with a read-only operational account where possible. Run before and after an
    authorized reconciliation and retain result sets with the incident/change record.
*/
SET NOCOUNT ON;

PRINT 'FLR-01. Ledger balance chain discontinuities within each seller and bucket';
;WITH LedgerChain AS
(
    SELECT bt.Id, bt.SellerId, bt.Bucket, bt.Type, bt.AmountIRR,
           bt.BalanceBeforeIRR, bt.BalanceAfterIRR, bt.Reference, bt.CreatedAtUtc,
           LAG(bt.BalanceAfterIRR) OVER
           (
               PARTITION BY bt.SellerId, bt.Bucket
               ORDER BY bt.CreatedAtUtc, bt.Id
           ) AS PreviousBalanceAfterIRR
    FROM dbo.BalanceTransactions AS bt
)
SELECT Id AS LedgerTransactionId, SellerId, Bucket, Type, AmountIRR,
       BalanceBeforeIRR, BalanceAfterIRR, PreviousBalanceAfterIRR,
       Reference, CreatedAtUtc
FROM LedgerChain
WHERE PreviousBalanceAfterIRR IS NOT NULL
  AND BalanceBeforeIRR <> PreviousBalanceAfterIRR
ORDER BY SellerId, Bucket, CreatedAtUtc, Id;

PRINT 'FLR-02. Current seller bucket differs from the latest recorded ledger snapshot for that bucket';
;WITH LatestBucketSnapshot AS
(
    SELECT bt.SellerId, bt.Bucket, bt.Id AS LedgerTransactionId,
           bt.BalanceAfterIRR, bt.CreatedAtUtc,
           ROW_NUMBER() OVER
           (
               PARTITION BY bt.SellerId, bt.Bucket
               ORDER BY bt.CreatedAtUtc DESC, bt.Id DESC
           ) AS rn
    FROM dbo.BalanceTransactions AS bt
)
SELECT sb.SellerId, bucket.Bucket, bucket.BucketName,
       bucket.CurrentBalanceIRR, latest.BalanceAfterIRR AS LatestLedgerBalanceAfterIRR,
       latest.LedgerTransactionId, latest.CreatedAtUtc AS LatestLedgerAtUtc,
       bucket.CurrentBalanceIRR - latest.BalanceAfterIRR AS DifferenceIRR
FROM dbo.SellerBalances AS sb
CROSS APPLY
(
    VALUES
        (CONVERT(tinyint, 1), N'Available', sb.AvailableIRR),
        (CONVERT(tinyint, 2), N'Pending', sb.PendingIRR),
        (CONVERT(tinyint, 3), N'Blocked', sb.BlockedIRR),
        (CONVERT(tinyint, 4), N'ReservedForSettlement', sb.ReservedForSettlementIRR),
        (CONVERT(tinyint, 5), N'Liability', sb.LiabilityIRR)
) AS bucket(Bucket, BucketName, CurrentBalanceIRR)
JOIN LatestBucketSnapshot AS latest
  ON latest.SellerId = sb.SellerId
 AND latest.Bucket = bucket.Bucket
 AND latest.rn = 1
WHERE bucket.CurrentBalanceIRR <> latest.BalanceAfterIRR
ORDER BY sb.SellerId, bucket.Bucket;

PRINT 'FLR-03. Non-zero seller buckets without any ledger snapshot (legacy/opening-balance review candidate)';
SELECT sb.SellerId, bucket.Bucket, bucket.BucketName, bucket.CurrentBalanceIRR
FROM dbo.SellerBalances AS sb
CROSS APPLY
(
    VALUES
        (CONVERT(tinyint, 1), N'Available', sb.AvailableIRR),
        (CONVERT(tinyint, 2), N'Pending', sb.PendingIRR),
        (CONVERT(tinyint, 3), N'Blocked', sb.BlockedIRR),
        (CONVERT(tinyint, 4), N'ReservedForSettlement', sb.ReservedForSettlementIRR),
        (CONVERT(tinyint, 5), N'Liability', sb.LiabilityIRR)
) AS bucket(Bucket, BucketName, CurrentBalanceIRR)
WHERE bucket.CurrentBalanceIRR <> 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.BalanceTransactions AS bt
      WHERE bt.SellerId = sb.SellerId
        AND bt.Bucket = bucket.Bucket
  )
ORDER BY sb.SellerId, bucket.Bucket;

PRINT 'FLR-04. Seller reserved balance versus active settlement state total';
;WITH ActiveSettlementTotals AS
(
    SELECT s.SellerId, SUM(s.AmountIRR) AS ExpectedReservedIRR,
           COUNT_BIG(*) AS ActiveSettlementCount
    FROM dbo.Settlements AS s
    WHERE s.Status IN (1, 2, 6) -- Requested, Processing, OnHold
    GROUP BY s.SellerId
)
SELECT COALESCE(sb.SellerId, ast.SellerId) AS SellerId,
       sb.ReservedForSettlementIRR,
       COALESCE(ast.ExpectedReservedIRR, 0) AS ExpectedReservedIRR,
       COALESCE(sb.ReservedForSettlementIRR, 0) - COALESCE(ast.ExpectedReservedIRR, 0) AS DifferenceIRR,
       COALESCE(ast.ActiveSettlementCount, 0) AS ActiveSettlementCount
FROM dbo.SellerBalances AS sb
FULL OUTER JOIN ActiveSettlementTotals AS ast ON ast.SellerId = sb.SellerId
WHERE sb.SellerId IS NULL
   OR ast.SellerId IS NULL
   OR sb.ReservedForSettlementIRR <> COALESCE(ast.ExpectedReservedIRR, 0)
ORDER BY COALESCE(sb.SellerId, ast.SellerId);

PRINT 'FLR-05. Settlement state has missing or duplicate reservation/final outcome ledger records';
;WITH SettlementLedgerCounts AS
(
    SELECT s.Id AS SettlementId, s.SellerId, s.AmountIRR, s.Status,
           SUM(CASE WHEN bt.Type = 4 AND bt.Bucket = 4
                         AND bt.Reference = N'SETTLEMENT_REQUESTED'
                         AND bt.AmountIRR = s.AmountIRR THEN 1 ELSE 0 END) AS ReservationPostingCount,
           SUM(CASE WHEN bt.Type = 4 AND bt.Bucket = 1
                         AND bt.AmountIRR = s.AmountIRR THEN 1 ELSE 0 END) AS SuccessfulPayoutPostingCount,
           SUM(CASE WHEN bt.Type = 14 AND bt.Bucket = 4
                         AND bt.AmountIRR = s.AmountIRR THEN 1 ELSE 0 END) AS FailureReleasePostingCount
    FROM dbo.Settlements AS s
    LEFT JOIN dbo.BalanceTransactions AS bt
      ON bt.SettlementId = s.Id
     AND bt.SellerId = s.SellerId
    GROUP BY s.Id, s.SellerId, s.AmountIRR, s.Status
)
SELECT SettlementId, SellerId, AmountIRR, Status,
       ReservationPostingCount, SuccessfulPayoutPostingCount, FailureReleasePostingCount,
       CASE
           WHEN ReservationPostingCount <> 1 THEN N'RESERVATION_COUNT_MISMATCH'
           WHEN Status = 3 AND SuccessfulPayoutPostingCount <> 1 THEN N'COMPLETED_OUTCOME_COUNT_MISMATCH'
           WHEN Status = 4 AND FailureReleasePostingCount <> 1 THEN N'FAILED_OUTCOME_COUNT_MISMATCH'
           WHEN Status IN (1, 2, 6)
                AND (SuccessfulPayoutPostingCount > 0 OR FailureReleasePostingCount > 0)
                THEN N'NONTERMINAL_WITH_FINAL_OUTCOME'
           WHEN Status = 3 AND FailureReleasePostingCount > 0 THEN N'COMPLETED_WITH_FAILURE_OUTCOME'
           WHEN Status = 4 AND SuccessfulPayoutPostingCount > 0 THEN N'FAILED_WITH_SUCCESS_OUTCOME'
           ELSE N'REVIEW'
       END AS Finding
FROM SettlementLedgerCounts
WHERE ReservationPostingCount <> 1
   OR (Status = 3 AND SuccessfulPayoutPostingCount <> 1)
   OR (Status = 4 AND FailureReleasePostingCount <> 1)
   OR (Status IN (1, 2, 6) AND (SuccessfulPayoutPostingCount > 0 OR FailureReleasePostingCount > 0))
   OR (Status = 3 AND FailureReleasePostingCount > 0)
   OR (Status = 4 AND SuccessfulPayoutPostingCount > 0)
ORDER BY SellerId, SettlementId;

PRINT 'FLR-06. Ledger rows with invalid bucket identifiers or negative balance snapshots';
SELECT bt.Id AS LedgerTransactionId, bt.SellerId, bt.SettlementId, bt.OrderId,
       bt.Type, bt.Bucket, bt.AmountIRR, bt.BalanceBeforeIRR, bt.BalanceAfterIRR,
       bt.Reference, bt.CreatedAtUtc
FROM dbo.BalanceTransactions AS bt
WHERE bt.Bucket NOT IN (1, 2, 3, 4, 5)
   OR bt.AmountIRR < 0
   OR bt.BalanceBeforeIRR < 0
   OR bt.BalanceAfterIRR < 0
ORDER BY bt.CreatedAtUtc, bt.Id;

# Financial Ledger and Seller Balance Reconciliation

## Purpose and safety boundary

Use `database/FinancialLedgerBucketReconciliation.sql` alongside `database/FinancialConsistencyChecks.sql` when investigating seller balance buckets and settlement lifecycle inconsistencies.

The new script is diagnostic-only. It does not modify data. A returned row is a review candidate, not authority to post a compensating transaction, change a balance, release a reservation, or alter a settlement status. Do not automatically repair the ledger, seller balances, payments, refunds, inventory, or settlements.

## Recommended procedure

1. Record the incident/change identifier, database name, UTC time, settlement IDs, and operator.
2. Run both consistency scripts with a read-only SQL account where possible. Securely retain the complete result sets.
3. For each affected seller, compare the five current buckets (Available, Pending, Blocked, ReservedForSettlement, Liability) with the ledger history for the same bucket.
4. Investigate chain discontinuities and current-vs-latest-snapshot differences using the full chronological transaction history. Timestamp ordering uses `CreatedAtUtc` and then `Id`; historical imports, old ledger semantics, or legacy opening balances may explain a finding and must be verified rather than assumed corrupt.
5. For settlements, verify the original `SETTLEMENT_REQUESTED` entry in the ReservedForSettlement bucket and exactly one final outcome matching the persisted status. A timeout or exception is ambiguous: retain reserved funds and obtain authoritative bank/provider evidence before reconciliation.
6. Check the settlement state, `SettlementReconciliationAudits`, `OutboxMessages`, bank reference, and operator evidence together. A ledger row by itself does not prove the bank transferred funds.
7. Only use the authorized application reconciliation flow after the bank/provider outcome is established. Never update `SellerBalances`, `BalanceTransactions`, or `Settlements` directly to silence a finding.
8. Re-run both scripts after the application transaction commits. Confirm that the intended finding is resolved and investigate any new findings.

## Query interpretation

- **FLR-01 — Bucket chain discontinuity:** a later ledger row's `BalanceBeforeIRR` differs from the prior row's `BalanceAfterIRR` for that seller/bucket. Confirm transaction ordering, legacy rows, and whether every balance mutation is represented.
- **FLR-02 — Current bucket differs from latest snapshot:** current aggregate balance differs from the latest recorded `BalanceAfterIRR` for that bucket. This can indicate an unlogged mutation or legacy/imported data; verify history before deciding.
- **FLR-03 — Non-zero bucket without ledger history:** the bucket may have an opening balance or a missing ledger history. This is a candidate, not a finding of fraud or proof of a broken transaction.
- **FLR-04 — Reserved balance differs from active settlements:** compare the aggregate with individual settlement reservation and outcome entries before any correction.
- **FLR-05 — Settlement ledger lifecycle:** each settlement should have one reservation entry; Completed should have one successful payout entry and one matching ReservedForSettlement release entry; Failed should have one definitive failure-release entry; non-terminal settlements must not have final outcomes. Older completed settlements may be reported if they predate the dual-bucket posting fix. Review the exact settlement ID and references to distinguish duplicates from legitimate historical semantics.
- **FLR-06 — Invalid bucket/snapshot values:** inspect schema constraints, imported rows, and the originating application path.

## Refund finalization preflight

Before a confirmed refund mutates the refund, order, payment, seller balance, or seller hold, the application verifies that the refund is still Processing and matches the order/payment/customer/total; the order is awaiting refund; the payment is successful and matches the order; and the balance and active hold belong to the same seller/order and the hold amount matches the order's seller share. It also preflights the eligible seller bucket before debiting it. A mismatch is a reconciliation exception, not a reason to consume a different seller's funds or force the refund to Completed. The operation must leave all aggregates and ledger rows unchanged when preflight fails.

## Transaction boundary expectations

Settlement reservation, settlement state, both affected balance buckets, all corresponding ledger postings, and the outbox event are expected to persist in one database transaction. Successful payout completion must record the release from ReservedForSettlement as well as the debit from Available; the manual reconciliation path follows the same two-bucket rule. Releasing a seller's complaint hold or closing the complaint window transfers value from Blocked to Available, so both bucket snapshots must be recorded in the same transaction. A posting for Blocked alone leaves the Available ledger stale even when the aggregate balance itself is correct. The external bank transfer is not part of that transaction. The application first claims a settlement as Processing, calls the gateway, and then finalizes it in a separate serializable transaction. If the gateway result is ambiguous, funds remain reserved and the settlement is held for manual reconciliation. If the bank returns a definitive result but database finalization fails, the application attempts to place the settlement OnHold; if persistence is unavailable, the remaining Processing state must be investigated.

These database checks cannot prove that a remote provider completed a transfer. They also cannot establish atomicity merely by looking at a successful final state; use SQL Server integration tests for transactional behavior and provider evidence for the external outcome.

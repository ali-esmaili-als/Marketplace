# Financial Gateway Reconciliation Runbook

This runbook covers ambiguous provider outcomes for refunds and seller payouts. It is an operational safety procedure, not a substitute for the provider's official transaction-status evidence.

## Non-negotiable rule

**A timeout is not proof of failure.** A provider may have completed a transfer even when the application did not receive the response. Never retry a refund or payout merely because the HTTP request timed out.

Use the provider's dashboard/API and bank statement to establish the final state first. Record the evidence and the operator's decision in the reconciliation note.

## Refunds

### How an ambiguous refund is represented

1. The application creates and persists a refund in Processing before calling the payment provider.
2. If the provider call throws or times out, the refund stays Processing; the order/payment are not marked refunded by that failed request path.
3. An active refund blocks another refund attempt for the same order. Do not work around this guard by creating a second refund record.
4. A refund can be reconciled only while its status is Processing.

### Reconcile a refund

- **Provider confirms refunded:** call the refund reconciliation operation with transferCompleted=true, the exact provider/bank reference, and a concise evidence note. The application completes the refund, updates the order and payment, applies the seller-balance/hold effects, records any commission reversal, and writes a RefundReconciliationAudit.
- **Provider confirms not refunded:** only after checking the final provider state, reconcile with transferCompleted=false and a note documenting the evidence. The refund becomes Failed; the eligible delivery-expiry flow can be retried after this definitive outcome.
- **Provider state remains unknown:** do not reconcile as failed and do not retry. Keep the refund in Processing, escalate to operations/provider support, and revisit when evidence is available.

## Seller payouts (settlements)

### How an ambiguous payout is represented

1. A settlement is claimed as Processing in a serializable database transaction before the payout gateway is called.
2. If the gateway throws or times out, the application changes the settlement to OnHold; the reserved seller funds remain reserved.
3. A settlement on hold must be reconciled. Do not start a second payout or manually release the reserved amount.

### Reconcile a settlement

- **Bank confirms paid:** reconcile with transferCompleted=true, the bank's reference, and an evidence note. The settlement becomes Completed, the reserved amount is consumed from seller funds, and the audit/ledger records are written.
- **Bank confirms not paid:** only after checking the final bank state, reconcile with transferCompleted=false and an evidence note. The settlement becomes Failed and the reservation is released back to withdrawable funds.
- **Bank state remains unknown:** keep the settlement on hold and keep the reservation intact. Escalate to the bank/provider; do not treat the timeout itself as a failed transfer.

A repeated processing request for a completed settlement returns its existing result without another gateway transfer. A settlement on hold is not automatically retried.

## Evidence and audit checklist

For every manual reconciliation, capture the following in the required note (do not include secrets or full credentials):

- Provider/bank checked and the time of the check (UTC).
- Provider transaction identifier and/or bank statement reference.
- Final status returned by the provider/bank, not merely the original API timeout.
- Operator/admin identity is recorded by the application.
- Decision: confirmed paid/refunded or confirmed not paid/refunded.
- Support ticket or incident reference, when applicable.

Before resolving an unknown transaction, verify the amount and currency, destination/source account, provider reference, and transaction timestamp. If evidence conflicts or is incomplete, leave the transaction unresolved and escalate.

## Important limitations

- Reconciliation actions are financially consequential and must be restricted to authorized administrators by the API authorization policy.
- This process does not itself query the bank/provider to prove the final outcome; the operator must obtain authoritative evidence before choosing the outcome.
- The application's unit tests use mocked persistence/gateways. They do not replace production SQL Server concurrency tests, provider sandbox certification, or operational reconciliation drills.


## Pre- and post-reconciliation database checks

Run `database/FinancialConsistencyChecks.sql` against the intended Marketplace database with a read-only operational account where possible. The script is diagnostic only: it must not be used as an automated repair script, and a returned row is a review item rather than permission to edit financial values directly.

### Recommended sequence

1. Capture the incident/change reference, database name, UTC timestamp, and the settlement/refund IDs under review.
2. Run the diagnostics before changing state. Save the result sets in the incident record, restricting access because they contain financial metadata.
3. For settlements, compare the settlement status with both ledger phases:
   - Reservation: `Type=Settlement`, `Bucket=ReservedForSettlement` (bucket value 4), amount equal to the settlement amount.
   - Successful payout finalization: `Type=Settlement`, `Bucket=Available` (bucket value 1), amount equal to the settlement amount.
   - Definitive payout failure: `Type=SettlementFailed` (type value 14), amount equal to the settlement amount.
4. Compare each seller's `ReservedForSettlementIRR` with the total of settlements in Requested, Processing, and OnHold. Differences may indicate a missing ledger/balance write, an out-of-band edit, or a code path that needs investigation; do not overwrite the balance based only on this aggregate.
5. Resolve the provider/bank outcome using authoritative evidence and the authorized reconciliation workflow. Do not directly update settlement statuses, seller balance buckets, or ledger rows with ad-hoc SQL.
6. Re-run the diagnostics after the reconciliation transaction commits. Confirm the intended anomaly is gone and investigate any newly surfaced mismatch.
7. If the provider outcome is known but the database cannot be safely finalized, preserve the reserved funds and escalate. Capture the original error, settlement/refund ID, provider reference, and audit evidence.

### Interpreting the expanded settlement checks

- **Missing final ledger entry:** a Completed settlement must have a matching Available-bucket settlement ledger entry. The reservation entry alone is not sufficient evidence that payout finalization was recorded.
- **Reserved-bucket mismatch:** the current reserved seller-balance bucket should equal the total amount for Requested, Processing, and OnHold settlements. Review both missing settlement records and duplicate/missing reservation ledger entries before any correction.
- **Duplicate final entries:** more than one Available-bucket final settlement entry for one settlement may indicate duplicate finalization or manual data changes.
- **Failed settlement amount mismatch:** the failure ledger amount should equal the settlement amount; mismatches require investigation before balances are adjusted.
- **Final ledger on a non-terminal settlement:** a Requested, Processing, or OnHold settlement with a final success/failure ledger entry is contradictory and should be escalated.
- **Missing reservation ledger entry:** a settlement without its original reserved-bucket entry needs a full history review even if the current bucket totals happen to balance.

These checks intentionally report inconsistencies instead of guessing a correction. A financially safe fix depends on provider evidence, the transaction history, and the exact failure point.

### Interpreting refund diagnostics (checks 17–26)

- **17 — Missing refund ledger:** a completed provider refund has no matching seller-side refund ledger row. Verify whether the seller amount was already removed at delivery expiry; do not create a compensating row until the original transaction history and balance buckets are understood.
- **18 — Multiple refund ledger rows for one order:** review every refund attempt and provider reference. Failed attempts may exist, but duplicate *financial debit effects* must not be assumed valid merely because multiple refund records exist.
- **19 — Seller mismatch:** a refund ledger row is associated with a seller other than the seller on the order. Treat this as a high-priority integrity incident.
- **20 — Order/payment not refunded:** the refund is marked Completed while its order or payment state disagrees. Confirm the provider result, then inspect the finalization transaction and audit trail.
- **21 — Processing refund with a terminal order/payment:** the order/payment appears finalized while the refund remains Processing. Check whether a previous commit partially applied outside the expected transaction boundary or whether an operator changed state manually.
- **22 — Commission reversal linkage:** validate that the reversal points to the same order as both the commission and refund, and that it does not exceed the original commission. Also inspect the total across all reversals (check 26).
- **23 — Multiple reconciliation audit entries:** repeated manual outcomes for a refund require review. Do not automatically classify multiple audit rows as corruption; establish their order and whether an earlier action was committed before a retry.
- **24 — Completed refunds exceed captured payment:** compare all completed refund records for the payment against the captured amount and the provider's transaction history. Stop additional refund activity until the excess is explained.
- **25 — Latest audit contradicts terminal refund status:** the latest recorded manual decision says the opposite of the refund's terminal state. Verify the authoritative provider outcome and the audit chronology.
- **26 — Commission over-reversal:** total reversed commission exceeds the commission charged. Reconcile the original commission, each refund, and all reversal rows before any financial correction.

These diagnostics are deliberately conservative. Some rows indicate an inconsistency; others are review signals that can be legitimate in specific histories. A query result is never, by itself, authority to alter a seller balance, refund status, payment status, commission, or ledger row.

### Refund incident workflow

1. Record the database/environment, UTC timestamp, order ID, payment ID, refund ID(s), provider reference, and incident ticket. Restrict access to the evidence.
2. Run the read-only financial diagnostics and retain all relevant result sets.
3. Query the payment provider's authoritative transaction history and verify the amount, currency, original payment, refund reference, final outcome, and timestamp. A timeout or a successful HTTP response alone is insufficient evidence.
4. If the provider confirms the refund was sent, use the authorized reconciliation operation with a bank/provider reference and a non-empty evidence note. If it confirms no refund was sent, reconcile as not transferred only after the final state is established.
5. If the provider state is still ambiguous, leave the refund Processing. Do not submit another refund, mark the order refunded manually, or release/consume seller funds with ad-hoc SQL.
6. After the reconciliation transaction commits, rerun diagnostics. Confirm the expected refund/order/payment/hold/ledger/audit outcome and investigate any remaining finding.
7. If the provider confirms a transfer but database finalization fails, preserve the existing state for reconciliation and escalate with the provider reference and exception details. Never replay the gateway refund simply to retry database persistence.

### Payment-to-ledger diagnostics (checks 27–30)

- **27 — Payment/order amount mismatch:** compare the persisted order total with the payment initiation snapshot and gateway receipt. Do not overwrite either amount until the cause (legacy data, incorrect payment creation, or corruption) is established.
- **28 — Captured/refunded payment without a sale ledger entry:** inspect the payment verification result, order lifecycle transaction, seller hold, and ledger history. For refunded orders, the original sale entry should remain auditable; the refund is a separate compensating movement.
- **29 — Sale ledger seller/amount mismatch:** compare the immutable order seller amount snapshot and seller identity against the ledger row. Treat a mismatch as a financial integrity incident, not a display issue.
- **30 — Multiple holds for one order:** inspect every hold's status and associated ledger movements. Do not sum, release, or consume holds manually until it is clear whether duplicate rows represent a committed duplicate or legitimate historical data.

For each finding, first preserve the query output and correlate payment provider references with order and ledger records. Checks 27–30 are read-only signals; they never justify direct SQL balance edits. After an authorized application-level reconciliation, rerun the relevant checks and retain the before/after evidence.

### Delivery and inventory diagnostics (checks 31–36)

- **31 — Paid-through-terminal order without a delivery row:** check payment confirmation and the order lifecycle transaction, including whether the delivery insert rolled back or historical data predates delivery tracking.
- **32 — Delivered/completed order without a delivered delivery record:** compare order and delivery timestamps and confirmation reference. This can indicate partial persistence or a legacy migration gap.
- **33 — Delivery says delivered but order state disagrees:** inspect the same transaction's order transition, balance movements, and inventory consumption before considering any application-level repair.
- **34 — Active reservation attached to a terminal order:** verify whether inventory release/consumption was committed. Do not release it a second time without checking the inventory row and reservation history.
- **35 — Inventory reserved count differs from active reservation totals:** compare per-variant inventory and every active reservation. A difference can block purchases or oversell stock; resolve through the supported inventory workflow, not a direct counter update.
- **36 — Delivery-code use timestamp conflicts with delivery completion:** compare the code verification audit trail, delivery confirmation reference, and delivery state. Preserve evidence before any corrective action.

Checks 34–36 can flag work that is in flight if the diagnostic is run concurrently with a transaction or background job. Re-run after the transaction/job has completed before escalating as a confirmed inconsistency. All checks remain read-only.

### Complaint resolution and seller-hold release safeguards

Before a completed order's complaint-window hold is released, the lifecycle operation must establish all of the following in the same serializable transaction:

- No active complaint remains for the order. If one exists, leave the order Delivered and the seller funds Blocked until the complaint is resolved.
- The seller balance belongs to the order's seller.
- The active hold belongs to the exact order and seller and its amount equals the immutable seller amount on the order.
- The blocked balance is sufficient for the hold amount.
- The order is Delivered and the complaint window has elapsed.

If any precondition fails, do not retry by changing status or balance rows directly. Preserve the order, hold, balance, and ledger records; inspect the complaint and hold relationships and use the application workflow after correcting the underlying issue. After a successful release, verify the order is Completed, the hold is Released, the blocked amount decreased exactly once, the available amount increased exactly once, and one matching hold-release ledger entry exists. A repeated close attempt must not create another financial movement.

### Complaint and seller-hold diagnostics (checks 37–44)

- **37 — Complaint party mismatch:** verify the complaint's customer and seller against the immutable order parties. Treat mismatches as an authorization/data-integrity incident; do not resolve or refund through that complaint.
- **38 — Active complaint outside Delivered state:** inspect the order transition and complaint transaction history. An open or under-review complaint must not coexist with an order already moved out of Delivered by a refund or completion flow.
- **39 — Customer-won complaint without refund progression:** confirm the resolution transaction requested a refund and that refund processing has not stalled. A completed refund may legitimately leave the complaint at CustomerWon while the order is Refunded.
- **40 — Seller-won complaint without completed order/released hold:** verify the complaint decision, complaint-window deadline, hold status, and hold-release ledger. Do not release funds manually to make the rows match.
- **41 — Active hold on a terminal order:** for Refunded, Completed, or Cancelled orders, verify that the hold was consumed or released in the same committed lifecycle operation. Confirm the actual ledger movement before taking corrective action.
- **42 — Terminal hold/order mismatch:** a released order hold should correspond to a Completed order; a consumed order hold should correspond to a Refunded order. Review refund and complaint chronology before classifying historical records as corruption.
- **43 — Multiple active complaints:** inspect concurrent requests and transaction isolation. The application should permit only one active complaint per order; do not close or cancel records directly in SQL to suppress the finding.
- **44 — Missing matching seller hold:** verify payment success finalization, order seller-amount snapshot, and hold creation transaction. Do not create a replacement hold or adjust balance buckets until the original payment/ledger history is established.

These checks are read-only diagnostics. Some results can arise from legacy or in-flight data; rerun after active transactions finish, correlate the provider/order/complaint/hold/ledger history, and use the authorized application workflow for any repair. Never fix a complaint or hold finding by directly editing financial balances or lifecycle statuses in SQL.

### Complaint/hold ledger evidence (checks 45–48)

- **45 — Delivered-through-completed order has an active hold but no matching ComplaintHold ledger entry:** compare the hold amount/seller with the order snapshot and blocked-bucket ledger. A refund or release must not be inferred solely from the current hold status.
- **46 — Released hold has no ComplaintHoldReleased ledger entry:** verify the completed-order payout transaction and the before/after blocked-balance values. A released status without its matching ledger movement needs investigation before another close/release attempt.
- **47 — Consumed hold has no ComplaintHoldConsumed ledger entry:** correlate the confirmed provider refund, refund completion transaction, hold state, and blocked-bucket ledger. Never replay a bank refund or manually consume the hold to silence this result.
- **48 — Complaint resolution evidence is missing or contradictory:** resolved complaints (CustomerWon/SellerWon) require both a nonblank resolution note and resolution timestamp; Open/UnderReview complaints should not already have resolution evidence. Review audit history and authorized resolution workflow.

For existing installations, apply `database/011_ActiveComplaintUniqueness.sql` after the earlier schema patches. It refuses to create the filtered unique index if duplicate active complaints already exist, so those rows must be reviewed through the supported workflow first. Fresh installations get the same index from `Marketplace_Complete.sql`. The database index is the final concurrency guard; application checks and serializable transactions remain necessary. Apply `database/012_ComplaintStatusConstraint.sql` as well to reject complaint status values outside the domain enum (Open=1, UnderReview=2, CustomerWon=3, SellerWon=4, Cancelled=5, Closed=6). It stops before changing the schema if invalid status values already exist. Fresh installations receive `CK_Complaints_Status` from `Marketplace_Complete.sql`. Apply `database/013_FinancialLifecycleStatusConstraints.sql` to an existing database to constrain seller-hold status (1–3), inventory-reservation status (1–4), delivery status (1–5), balance-transaction type (1–14), and balance bucket (1–5). It checks existing rows first and aborts with a targeted error if values are outside the domain ranges. Fresh installations receive these five checks from `Marketplace_Complete.sql`. The SQL Server integration suite executes this patch against the bootstrapped schema, verifies trusted constraints, reruns it, and confirms invalid transaction type/bucket values are rejected.



### Seller balance snapshot and ledger ownership (checks 49–52)

- **49 — Current seller balance differs from the latest ledger snapshot for a bucket:** compare the latest transaction for that seller/bucket with the current bucket amount. The query also flags a nonzero bucket with no ledger entry. Check transaction ordering, transaction type, and the relevant order/settlement workflow before taking action. Do not update `SellerBalances` directly to make the numbers match.
- **50 — Ledger seller differs from the referenced order's seller:** verify the order snapshot and transaction provenance. This can indicate a wrong seller association or a manually inserted ledger row.
- **51 — Ledger seller differs from the referenced settlement's seller:** compare settlement ID, seller ID, reservation/release entries, and provider reconciliation evidence. Do not retry a bank transfer merely because the ledger association is inconsistent.
- **52 — More than one Sale ledger entry exists for an order:** verify payment capture and the order's sale posting history. The unique index protects supported writes where installed; the diagnostic remains useful for older databases or manually imported rows.

These are read-only checks. Run them after in-flight financial transactions have settled, preserve the returned transaction IDs and timestamps, and use the authorized reconciliation workflow. A discrepancy is evidence to investigate, not permission to repair balances with ad-hoc SQL.


## Core payment lifecycle status constraints (patch 014)

The fresh bootstrap schema constrains persisted status values for `Orders`, `Payments`, `PaymentTransactions`, `Refunds`, and `Settlements` to the ranges used by the domain enums. Existing databases must apply `database/014_PaymentRefundSettlementStatusConstraints.sql` after backing up and reviewing existing data.

The patch validates all five tables before adding trusted SQL Server check constraints. If it stops because invalid status values exist, identify and investigate those rows first; do not bulk-map unknown statuses to a valid value, because that can misrepresent financial history. The patch is safe to re-run after a successful application.

The SQL Server integration suite simulates an older schema by dropping the five constraints, applies the patch, verifies that all constraints are enabled and trusted, re-applies it, and attempts an invalid status write for each protected table. All five writes must be rejected by SQL Server.


## Payment authority and active-refund idempotency (patch 015)

The fresh schema uses two filtered unique indexes as the final concurrency guard:

- `UX_PaymentTransactions_Provider_Authority` prevents the same non-null authority from being recorded twice for the same provider.
- `UX_Refunds_OneActivePerOrder` permits at most one refund in Requested, Approved, or Processing status per order. Failed and Rejected refunds are not active, so a new attempt after a definitive failure remains possible; a completed refund is terminal and does not need an active slot.

For existing databases, apply `database/015_FinancialIdempotencyIndexes.sql` after reviewing duplicates. It deliberately aborts if duplicate authorities or multiple active refunds already exist; do not delete rows merely to make the migration pass. Establish each gateway outcome from provider records and reconcile financial history first.

The SQL Server integration suite drops both indexes to simulate an older schema, applies and re-applies the patch, confirms duplicate authority and duplicate active-refund writes are rejected, and confirms a new refund can be created after the earlier attempt is marked Failed. It also seeds duplicate legacy records before migration and verifies that patch 015 aborts with a targeted error while preserving all records; only after the test explicitly resolves each duplicate does the migration proceed. This database guard complements—not replaces—the application-level active-refund check and serializable transaction.


The read-only financial diagnostics now also include checks 53–54 for duplicate provider/authority pairs and multiple active refund attempts per order. Run these checks before applying patch 015 to legacy databases; they help identify the affected payment/refund records, but do not determine the gateway's final financial outcome by themselves.


## Payment verification timeouts and transaction-state diagnostics

A timeout or transport exception during payment verification is an **unknown provider outcome**, not a definitive rejection. The verification path must leave the payment and its initiated provider transaction unchanged so the same payment/authority can be safely verified again. Only an explicit unsuccessful verification response may take the definitive-failure path. A payment already marked Succeeded is idempotent and must not call the gateway again.

The read-only diagnostics now include:

- **Check 55:** a provider transaction is marked Succeeded while its payment is neither Succeeded nor ReconciliationRequired. The latter status is permitted when the bank confirmed payment but local financial finalization failed and requires reconciliation.
- **Check 56:** a payment is marked Succeeded but has no successful provider transaction.

For either result, compare the provider's final status/reference with the payment, order, seller ledger, and reconciliation audit before making any correction. Never mark an ambiguous transaction failed solely to make the statuses agree.


The payment diagnostics also include checks 57–59:

- **Check 57:** a successful provider transaction's amount differs from the immutable payment amount.
- **Check 58:** a provider transaction is marked successful but has no bank reference.
- **Check 59:** a payment is marked Succeeded without both its final reference and paid timestamp.

These findings must be reconciled against the gateway's authoritative transaction record. Do not edit the amount, reference, or timestamps simply to silence a diagnostic; preserve provider evidence and use the payment reconciliation workflow if local finalization did not complete. The SQL Server integration test deliberately seeds each divergence and verifies that the corresponding read-only predicates find it.


The payment and provider-transaction domain entities now reject blank bank references before entering Succeeded state. This prevents new invalid records through normal domain transitions; checks 58–59 remain useful for legacy data, direct SQL writes, and incidents where older application versions bypassed this validation. The rejection occurs before mutating status, reference, or paid timestamp, so callers can safely correct the input without leaving a partially transitioned aggregate.

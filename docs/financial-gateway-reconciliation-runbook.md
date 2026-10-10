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


## Payment verification timeout and buyer recovery

A payment verification request that times out is not a definitive rejection. The verification application service leaves the persisted payment and its latest transaction unchanged when the gateway throws before returning a verification result. In particular, a redirected payment remains redirected; the customer must not interpret an HTTP timeout as proof that no charge occurred.

Operational and client behavior:

1. Keep the original payment ID and authority. Do not create a replacement payment solely because verification timed out.
2. Re-fetch the order/payment status from the Marketplace API. If the gateway outcome is still unknown, keep showing the current non-success state and provide a safe status refresh.
3. Show a payment reference only when the API returns a persisted reference for a successful or refunded payment; do not synthesize a bank reference from the authority in the client.
4. If a subsequent authoritative verification confirms success, the lifecycle service finalizes the payment and financial effects idempotently. If the gateway returns an explicit definitive rejection, the current verification contract treats that result as failure.
5. If the provider cannot distinguish definitive rejection from an uncertain result in its response, do not map the uncertain response to a definitive rejection. The gateway adapter contract must expose that distinction before such a provider is enabled for production.

The existing automated timeout tests use mocked gateways and persistence. They establish that an exception/timeout does not mutate payment/transaction state, but they do not prove a real provider's semantics. Validate each production provider's definitive-rejection and ambiguous-response mapping against its official protocol and sandbox before enabling it.

### Interpreting lifecycle diagnostics (checks 65–68)

- **65 — Hold/order snapshot mismatch:** a seller-balance hold's seller or amount differs from the order's immutable seller-share snapshot. Treat this as a high-priority integrity incident; do not consume, release, or recreate the hold until the order, payment, and ledger history are traced.
- **66 — Missing seller hold:** an order that progressed from payment into a later lifecycle state has no seller-balance hold. Verify whether payment finalization committed partially, whether legacy data predates the hold workflow, and whether a migration or out-of-band edit occurred. Do not create a hold from current balance totals without reconstructing the original transaction.
- **67 — Hold/order terminal-state conflict:** a consumed hold is expected with a refunded order, while a released hold is expected with a completed order. Check the refund/complaint decision, balance transactions, and audit trail; a row is a review signal, not a safe automatic repair instruction.
- **68 — Multiple successful provider transactions for one payment:** inspect every authority/reference and the provider's transaction history. Confirm whether the bank captured more than once. Do not assume duplicate rows are duplicate charges, and do not issue a compensating refund until the external outcome and local ledger effects are established.

These checks complement payment/order, refund/commission, inventory-reservation, and settlement diagnostics. Run them before and after an authorized reconciliation. They are intentionally read-only and do not infer a corrective balance adjustment.

### Successful gateway response for a cancelled or still-pending order

The payment verification path must not turn a cancelled order back into a payable/fulfillable order merely because a gateway later reports success. If a definitive success cannot be committed together with the expected order transition, the payment is moved to ReconciliationRequired and its provider reference is retained.

1. Verify the provider authority, amount, currency, and final bank status directly from the provider's transaction history.
2. Trace the order, payment attempts, inventory reservations, seller balance hold, and ledger entries with the order financial trace and read-only consistency checks.
3. If the bank did not capture funds, document the provider's definitive rejection and resolve the reconciliation case without creating seller funds.
4. If the bank did capture funds, keep the payment in reconciliation until an authorized, auditable recovery decision is made. Do not recreate the order, reserve inventory again, or credit seller balance based only on the callback.
5. Record the provider reference and the operator's evidence in the reconciliation audit. Confirm inventory availability and customer communication separately from financial settlement.

A successful gateway response and a successful order finalization are separate facts. The first must never be treated as proof that inventory, seller funds, and order state were all committed.

### Inventory reservation consistency checks (69–71)

- **Check 69 — active reservation on a delivered or terminal order:** treat as a stock-accounting exception. Verify the order transition and reservation/stock movement history before correcting either record.
- **Check 70 — inventory reserved quantity differs from active reservation rows:** compare the inventory item and all reservation rows for the variant. This can indicate a partial write, legacy data, or a double release/consume. Do not update ReservedQuantity by guesswork; reconcile quantities against the order and stock movement evidence.
- **Check 71 — expired reservation on a pending-payment order:** this is a cleanup backlog candidate, not proof that the order should be cancelled immediately. Check payment status and provider outcome first. If a payment was captured or its outcome is ambiguous, use the payment reconciliation process before releasing stock.

These checks are read-only. Run them before and after an authorized recovery and retain the result with the case audit. Never release a reservation while a payment could still have been captured without first establishing the provider's final outcome.


### Settlement reserve and outcome diagnostics (72–74)

- **Check 72 — reserved seller balance differs from active settlement requests:** compare the seller balance's reserved amount with the total of Requested, Processing, and OnHold settlements. Investigate missing settlement rows, duplicate reservations, failed finalization, or out-of-band balance edits. Do not alter the balance or settlement status until the ledger and provider outcome are reconstructed.
- **Check 73 — completed settlement has no bank reference:** verify the payout directly with the payout provider/bank and inspect the settlement reconciliation audit. Do not treat the Completed status alone as proof of a traceable transfer.
- **Check 74 — completed/failed settlement has fewer than two settlement-linked ledger rows:** the expected lifecycle includes the initial reservation entry and a final completion/failure entry. Review the settlement, balance snapshots, and transaction history; older or migrated data may need contextual review before correction.

Checks 72–74 are read-only diagnostics. A non-empty result is a reconciliation lead, not an instruction to recreate ledger entries or change balances automatically.


### Gateway-confirmed payment when local finalization fails unexpectedly

A provider's definitive success response is not enough to prove that the order, inventory lifecycle, seller hold, and pending ledger entries were committed. If the payment finalization transaction fails for a non-domain reason, the application now makes a best-effort attempt to mark the payment `ReconciliationRequired` while retaining the gateway reference. If the recovery write also fails (for example, the database is unavailable), the original exception is preserved; the payment may still appear Redirected/Pending until the database recovers.

1. Search the provider by the original authority and confirm the final captured amount/reference.
2. Inspect the persisted payment and order state, inventory reservations, seller hold, delivery record, and seller ledger.
3. If the payment is `ReconciliationRequired`, use the payment reconciliation workflow; do not start another checkout or credit seller funds directly.
4. If the recovery write could not persist, rerun the read-only consistency checks once SQL Server is healthy and reconcile using the provider's definitive status and the complete order trace.
5. Do not mark a payment failed or release inventory solely because the finalization request returned an exception after the bank confirmed success.

The regression test for this path simulates a persistence exception before the lifecycle transaction can run, then verifies that the best-effort recovery records the payment as requiring reconciliation. It does not simulate a real SQL Server outage or prove that the recovery write will succeed during an outage.


### Refund amount, commission reversal, and stale-processing diagnostics (75–79)

- **Check 75 — completed refund amount differs from the order/payment snapshot:** the current application implements full-order refunds. Verify the order total, captured payment amount, provider transfer amount, and refund record. If partial refunds are introduced later, revise this check together with the refund domain contract before enabling them.
- **Check 76 — completed refund lacks a commission reversal for its exact refund and commission:** inspect the refund-linked reversal and original commission. Do not create a reversal based only on the aggregate seller balance; confirm the provider transfer and refund lifecycle first.
- **Check 77 — failed/rejected refund has a refund ledger posting or commission reversal:** investigate whether the refund was actually transferred, whether a later manual reconciliation changed the financial outcome, and whether the wrong refund identity was attached. Do not delete a ledger row to make the query clear.
- **Check 78 — payment marked Refunded has no completed refund:** compare provider transaction history, payment status, order state, and refund audits. The payment's terminal status alone does not establish which refund transfer occurred.
- **Check 79 — refund has remained Processing for over 30 minutes:** this is an operational triage threshold, not an automatic timeout. Confirm the provider's authoritative refund result before calling `ReconcileAsync`; a timeout or exception can mean the bank completed the transfer.

Checks 75–79 are read-only. The 30-minute threshold is intentionally a review signal and must not trigger automatic failure, retry, or balance release. Current refund processing is a full-refund flow; any future partial-refund feature must update the amount diagnostics and financial invariants in the same change.


### Payout confirmed but settlement finalization cannot be persisted

The payout provider can return a definitive result while the subsequent SQL transaction that updates the settlement, seller balance, and final ledger row fails. The service makes a best-effort recovery transition from Processing to OnHold after such a finalization exception, without releasing reserved funds. The original persistence exception is preserved for logging and alerting. If the recovery write also fails, the settlement may remain Processing and requires operational review after the database recovers.

1. Do not retry the payout or manually release the reserve.
2. Query the bank/provider using the settlement's immutable bank-account snapshot, amount, and any returned reference.
3. Inspect settlement status, reservation and outcome ledger entries, seller balance snapshots, and outbox events.
4. If the record is OnHold, use the audited settlement reconciliation workflow only after confirming the provider's final transfer result.
5. If it remains Processing because the recovery write could not persist, restore database availability and reconcile the original settlement before any payout retry.

A regression test covers a successful provider response followed by a simulated finalization persistence exception. It verifies the settlement is put on hold, reserved funds remain unchanged, the original exception is rethrown, and the payout provider is called only once.


### Refund ledger identity and commission snapshot diagnostics (80–85)

- **Check 80 — completed refund lacks its exact refund ledger identity:** inspect the refund, seller-balance movement, and original order. A zero-value seller debit can be valid when delivery-expiry processing already removed the seller share; the refund-linked row must still exist to explain the lifecycle.
- **Check 81 — refund ledger row disagrees with the order/seller or expected debit:** the current full-refund path records either zero seller debit (already removed) or the order's seller share. Confirm the delivery-expiry path and bucket snapshots before changing anything.
- **Check 82 — commission reversals exceed the original commission:** inspect every refund and reversal for the commission. Do not delete or rewrite reversal records to force the total into range.
- **Check 83 — commission snapshot differs from the order financial snapshot:** compare the immutable order and commission split, including shipping treatment and historical pricing rules. If older versions used a different snapshot contract, classify those rows before treating them as corruption.
- **Check 84 — refund's payment/customer identity or amount conflicts with the order:** verify the original payment, order ownership, captured amount, and refund record. Do not execute a second provider refund while identities are inconsistent.
- **Check 85 — multiple commission snapshots exist for one order:** inspect commission creation retries and ledger references. The current model expects one commission aggregate per order; do not consolidate rows without tracing associated balance transactions and reversals.

Checks 80–85 are read-only investigation queries. They are designed to find inconsistent identities and aggregate totals; they do not automatically repair ledger history. In particular, check 83 should be interpreted against the commission snapshot contract that was active when the order was created.


### Payout timeout when the OnHold recovery write also fails

If the payout provider throws or times out, the transfer outcome is ambiguous. The service attempts to move the settlement from `Processing` to `OnHold` without releasing the reserved funds. If this recovery write also fails, the original provider exception is preserved and the settlement may remain `Processing`.

1. Do not retry the payout and do not release the seller's reservation.
2. After database availability is restored, inspect the settlement and its reservation ledger entries.
3. Confirm the bank's final transfer status using the settlement amount, immutable bank-account snapshot, and any provider reference.
4. Use the audited reconciliation flow only after the provider's final status is established.

Failure to persist the recovery state is not evidence that the payout failed. A regression test verifies that the original bank timeout is preserved, funds remain reserved, and the payout is invoked only once.

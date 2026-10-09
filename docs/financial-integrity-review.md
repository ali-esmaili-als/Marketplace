# Financial integrity review workflow

The admin financial-integrity endpoints are read-only diagnostics unless the endpoint explicitly records an audit note. They do not repair balances or change payment, order, refund, settlement, inventory, seller-balance, or ledger state.

## Endpoints

- `GET /api/admin/financial-integrity/summary`: aggregate payment, refund, and settlement diagnostic counts.
- `GET /api/admin/financial-integrity/items`: bounded detail lists for payment/order mismatches, processing refunds, and on-hold settlements.
- `GET /api/admin/financial-integrity/order-flows`: checks commission/order amount and seller alignment, completed refunds missing seller-ledger entries, completed refunds missing commission reversals, and commission-reversal links/amounts. The response returns up to 200 detailed findings and exact total counts by type. New refund ledger postings carry `RefundId` and are matched exactly; legacy rows with no `RefundId` use an order/seller fallback and still require manual corroboration when an order has historical retry attempts.
- `GET /api/admin/financial-integrity/order-trace/{orderId}`: read-only end-to-end trace for one order, including bounded payment attempts/provider transactions, commission, refunds, commission reversals, linked ledger entries, current seller balance, and the seller’s 50 latest settlements. It returns internal consistency findings and `itemsTruncated` when a section reaches its cap.
- `GET /api/admin/financial-integrity/ledger`: compares each seller balance bucket with the latest ledger snapshot for that seller/bucket, identifies non-zero buckets without ledger history, missing SellerBalance rows, and mismatches between the reserved balance and active settlement totals. Returns up to 200 findings plus total counts and a truncation flag.
- `POST /api/admin/financial-integrity/reviews`: records an administrator's review note in `AdminAuditEvents`.
- `GET /api/admin/financial-integrity/reviews`: returns up to 200 most recent review notes.
- `GET /api/admin/financial-integrity/cases`: returns the latest append-only case status for each `(kind, entity ID)` pair, up to 500 cases, based on the latest 2,000 status events.
- `POST /api/admin/financial-integrity/cases`: appends a case status event. Supported statuses are `Open`, `InProgress`, `AwaitingEvidence`, `Resolved`, and `FalsePositive`; every change requires a note.
- `GET /api/admin/financial-integrity/cases/{kind}/{entityKey}/history`: returns up to 200 append-only status-change and recheck events for one case.
- `POST /api/admin/financial-integrity/cases/{kind}/{entityKey}/recheck`: re-evaluates the current database predicate for that finding kind and appends the result to `AdminAuditEvents`.

All endpoints require `Admin.Settlement.Process`.
## Financial case workflow

Case status events are stored in `AdminAuditEvents` with action `FinancialIntegrity.CaseStatusChanged`; recheck events use `FinancialIntegrity.CaseRechecked`. Both are append-only. The case list uses the latest 2,000 activity events before grouping and returns at most 500 cases; `itemsTruncated` warns when the event bound is reached. The per-case history endpoint returns up to 200 events and sets `itemsTruncated` when the bound is reached. The recheck endpoint evaluates current database predicates for the requested finding kind, records the current statuses and result, and does not update the financial record. `PaymentOrderMismatch` checks the same payment/order status combinations used by the diagnostic items endpoint; `PaymentReview`, `RefundProcessing`, and `SettlementOnHold` check whether the payment remains `ReconciliationRequired`, refund remains `Processing`, or settlement remains `OnHold`, respectively. A clear recheck only means that the predicate for that finding kind is no longer true at check time; it does not prove a refund reached the customer, a settlement reached the seller, or the ledger is reconciled. `Resolved` means the investigation workflow was marked complete, not that the underlying financial mismatch was automatically corrected or that a bank transfer was independently verified. `FalsePositive` records the administrator's assessment and likewise does not alter payments, refunds, settlements, seller balances, or ledger entries. The UI must keep diagnostic findings visible until the underlying diagnostic condition no longer exists. Recheck events count as case activity but do not change the latest workflow status.


For an existing database, apply `database/018_RefundLedgerIdentity.sql` before running the updated `database/FinancialConsistencyChecks.sql`. Fresh databases created from `database/Marketplace_Complete.sql` already include the new nullable `RefundId` column, foreign key, and filtered unique index. The migration intentionally does not infer `RefundId` for historical ledger rows.

## Ledger comparison caveats

New refund ledger postings reference `RefundId`, with a filtered unique index preventing more than one refund ledger row per refund. Existing ledger rows remain nullable for backward compatibility; historical rows are not auto-linked because retry history cannot be safely inferred. The latest `BalanceTransactions.BalanceAfterIRR` is treated as the last recorded snapshot for its specific bucket. The endpoint compares it with the current `SellerBalances` value; it does not reconstruct the entire ledger, infer missing bank transfers, or automatically correct either side. The reserved-bucket comparison also checks active settlement totals for Requested, Processing, and OnHold statuses. Investigate each finding against the underlying records before any correction.

## Review request

```json
{
  "kind": "PaymentOrderMismatch",
  "entityKey": "123",
  "note": "Compared provider receipt and order history; follow up with finance."
}
```

Supported review kinds are `PaymentOrderMismatch`, `PaymentReview`, `RefundProcessing`, and `SettlementOnHold`. Entity keys are positive numeric IDs and must refer to an existing payment, refund, or settlement, as appropriate.

Review notes are persisted as an `AdminAuditEvent` with action `FinancialIntegrity.Reviewed`, entity type, entity key, actor, timestamp, correlation ID, and JSON details. A review entry is an audit trail, not a resolution flag: the finding remains in the diagnostic list until the underlying financial state is corrected through its dedicated workflow.

Do not record credentials, full bank account details, payment-card data, or other secrets in review notes. Internal consistency checks are not a substitute for reconciliation against a bank statement or a provider's authoritative transaction API.


## Order financial trace

The admin order trace endpoint requires `Admin.Settlement.Process` and does not mutate financial records. It is intended for investigation by order ID, not as a bank confirmation or a reconstructed double-entry ledger. It checks for missing payment records, payment/order amount mismatch, a succeeded payment with an order still pending payment, missing commission, commission/order seller or amount mismatch, missing sale ledger entries, completed refunds without a matching refund ledger row, missing commission reversals, and missing seller balance rows.

The endpoint bounds the order's payment attempts to 100, provider transactions to 300, refunds to 100, commission reversals to 200, matching ledger transactions to 500, and seller settlement history to 50. The response sets `itemsTruncated` if any section reaches its cap. Historical refund ledger entries with a null `RefundId` are matched only by the legacy order ID and refund transaction type; investigate historical retry cases manually before asserting a definitive match.

The seller balance and settlement list are seller-level pooled finance data. A settlement is associated with the selected order only where a ledger row explicitly links that settlement to the order/refund trace; the endpoint does not allocate pooled settlement amounts to individual orders. A clean result means only that the listed predicates did not detect a mismatch in the returned data. It does not independently verify bank transfers, payment-provider statements, or customer receipt of a refund.


## End-to-end trace SQL integration coverage

`tests/Marketplace.SqlServer.IntegrationTests/OrderFinancialTraceIntegrationTests.cs` seeds an isolated SQL Server database from the complete bootstrap schema and verifies the key relationships used by the admin order trace:

- order to payment and provider transaction;
- order to commission and seller amount;
- completed refund to its payment and order;
- commission reversal to the exact commission/refund pair;
- seller balance and seller-level settlement linked through an explicit settlement ledger entry;
- detection of a completed refund after its refund-linked ledger entry is removed;
- rejection of duplicate commission reversal for the same commission/refund pair.

The test uses the `MARKETPLACE_SQLSERVER` integration-test connection, creates and drops its own dedicated database, and does not connect to or mutate any production database. It verifies database relationships and diagnostic predicates; it does not call a live bank, prove settlement reached a bank account, or replace HTTP endpoint authorization/response tests.

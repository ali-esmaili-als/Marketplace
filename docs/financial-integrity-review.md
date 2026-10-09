# Financial integrity review workflow

The admin financial-integrity endpoints are read-only diagnostics unless the endpoint explicitly records an audit note. They do not repair balances or change payment, order, refund, settlement, inventory, seller-balance, or ledger state.

## Endpoints

- `GET /api/admin/financial-integrity/summary`: aggregate payment, refund, and settlement diagnostic counts.
- `GET /api/admin/financial-integrity/items`: bounded detail lists for payment/order mismatches, processing refunds, and on-hold settlements.
- `GET /api/admin/financial-integrity/ledger`: compares each seller balance bucket with the latest ledger snapshot for that seller/bucket, identifies non-zero buckets without ledger history, missing SellerBalance rows, and mismatches between the reserved balance and active settlement totals. Returns up to 200 findings plus total counts and a truncation flag.
- `POST /api/admin/financial-integrity/reviews`: records an administrator's review note in `AdminAuditEvents`.
- `GET /api/admin/financial-integrity/reviews`: returns up to 200 most recent review notes.

All endpoints require `Admin.Settlement.Process`.

## Ledger comparison caveats

The latest `BalanceTransactions.BalanceAfterIRR` is treated as the last recorded snapshot for its specific bucket. The endpoint compares it with the current `SellerBalances` value; it does not reconstruct the entire ledger, infer missing bank transfers, or automatically correct either side. The reserved-bucket comparison also checks active settlement totals for Requested, Processing, and OnHold statuses. Investigate each finding against the underlying records before any correction.

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

# Financial integrity review workflow

The admin financial-integrity review endpoints are audit-only. They do not change payment, order, refund, settlement, inventory, seller balance, or ledger state.

## Endpoints

- `GET /api/admin/financial-integrity/summary`: aggregate diagnostic counts.
- `GET /api/admin/financial-integrity/items`: bounded detail lists for payment/order mismatches, processing refunds, and on-hold settlements.
- `POST /api/admin/financial-integrity/reviews`: records an administrator's review note in `AdminAuditEvents`.
- `GET /api/admin/financial-integrity/reviews`: returns up to 200 most recent review notes.

All endpoints require `Admin.Settlement.Process`.

## Review request

```json
{
  "kind": "PaymentOrderMismatch",
  "entityKey": "123",
  "note": "Compared provider receipt and order history; follow up with finance."
}
```

Supported kinds are `PaymentOrderMismatch`, `PaymentReview`, `RefundProcessing`, and `SettlementOnHold`. Entity keys are positive numeric IDs and must refer to an existing payment, refund, or settlement, as appropriate.

Review notes are persisted as an `AdminAuditEvent` with action `FinancialIntegrity.Reviewed`, entity type, entity key, actor, timestamp, correlation ID, and JSON details. A review entry is an audit trail, not a resolution flag: the finding remains in the diagnostic list until the underlying financial state is corrected through its dedicated workflow.

Do not record credentials, full bank account details, payment-card data, or other secrets in review notes. This internal consistency report is not a substitute for reconciliation against a bank statement or a provider's authoritative transaction API.

# Refund lifecycle and customer visibility

Order detail responses now include the latest persisted refund for both the owning customer and the owning seller:
- refund ID and amount in IRR
- status and reason
- request and completion timestamps
- provider reference when recorded

The response intentionally does not expose internal reconciliation notes or failure diagnostics. Admin-only audit and reconciliation details remain available in the administrative refund workflow.

## Safe retry behavior

A refund in Requested, Approved or Processing is considered active. SQL Server enforces one active refund per order, and the service reserves the refund in a serializable transaction before calling the external gateway. If the gateway times out or throws after the request may have reached the bank, the refund stays Processing and repeat customer requests are rejected until an administrator reconciles the outcome.

Only a confirmed successful transfer enters the common finalization path that updates the refund, order, payment, seller hold/balance, refund ledger identity and commission reversal atomically. A timeout is not converted into a definitive failure and must not cause a second automatic bank refund.

The refund aggregate enforces the same safety boundary: `Fail` is allowed only after gateway processing has started, and `Reject` is allowed only before processing begins. A `Processing` refund cannot be rejected to clear the active-refund guard; it must remain reserved until a definitive bank outcome is established and recorded through reconciliation.

## Verification

The SQL Server integration test `RefundConcurrencyIntegrationTests` runs two refund requests against separate DbContexts. It blocks the first request at the gateway, submits a concurrent second request, then simulates a timeout. Assertions verify one gateway call, one Processing refund, unchanged successful payment state, and no premature ledger movement.

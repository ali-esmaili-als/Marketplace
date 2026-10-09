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

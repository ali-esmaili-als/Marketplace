# Transactional Outbox

## What is implemented

Settlement lifecycle transitions write an `OutboxMessages` row through the same EF Core `DbContext` and transaction as the settlement/balance/ledger update. If the business transaction rolls back, the event row rolls back too.

Current event types emitted by `SettlementService`:

- `Settlement.Requested`
- `Settlement.Processing`
- `Settlement.Completed`
- `Settlement.Failed`
- `Settlement.OnHold`
- `Settlement.Reconciled`

Payloads contain settlement identifiers, amount, status and (where relevant) the bank reference. Bank account snapshots such as IBAN are deliberately not included.

## Delivery semantics

The table and domain lifecycle support a durable queue with attempts, retry time, processing lease, last error and dead-letter state. A worker can reclaim a `Processing` row after `LockedUntilUtc` expires. Consumers must deduplicate by `MessageId`: delivery across process crashes is expected to be **at least once**, not exactly once.

A lease-based hosted dispatcher is now implemented with atomic SQL claiming, per-claim lease tokens, retry backoff and dead-letter handling. It remains disabled by default. A real transport-specific `IOutboxPublisher` must be registered before enabling `Outbox:Enabled`; with no publisher registered, the worker does not claim rows. Do not treat rows as delivered to an external broker or notification provider until that integration is configured and verified. Configure and test that integration before relying on downstream delivery. Set `Outbox:Enabled=true` only after registering the publisher and validating its deduplication behavior.

## Database deployment

- New empty database: run `database/Marketplace_Complete.sql`.
- Existing database: run `database/020_TransactionalOutbox.sql` once; it is idempotent.
- Monitor `Pending`, `Processing`, and `DeadLetter` counts before enabling a publisher.
- Do not manually mark financial outbox rows processed to fix balances. Outbox delivery status is operational metadata and must never mutate the financial ledger or seller balances.

## Reliability boundaries

The outbox closes the gap between committing a settlement state transition and persisting its event. It does not make a bank transfer exactly-once. For ambiguous bank responses, keep funds reserved and reconcile with the provider's status lookup or idempotency support before deciding whether to release funds or record completion.

# Marketplace database bootstrap and upgrade policy

## Canonical source for new databases

Use `database/Marketplace_Complete.sql` to create a **new, empty** Marketplace database. It is the canonical bootstrap schema and includes the current commerce, identity/authorization, financial lifecycle, reconciliation/audit, provider configuration, sequence, and transactional Outbox objects. It validates required tables at the end.

The SQL Server integration-test suite applies this exact file to a newly created database before checking schema constraints and running read-only financial diagnostics. The bootstrap integration test also asserts the presence of:

- `dbo.OutboxMessages`, `LockToken`, and `IX_OutboxMessages_Poll`
- `dbo.AdminAuditEvents` and its user foreign key
- `dbo.MarketplaceSequence`

Do not run the bootstrap against a database containing application data. It is not a general-purpose upgrade script: it creates the schema and seeds reference/configuration data, and is not designed to reconcile every possible older schema safely.

## Existing databases

The numbered scripts are retained as **incremental upgrade history** for databases created before the canonical bootstrap was introduced. They must not be deleted merely because their final schema is represented in the bootstrap file.

| Script | Purpose |
| --- | --- |
| `004_Identity.sql`–`010_SmsProviderSettings.sql` | Earlier identity, seller/catalog, pricing, notifications, delivery, balance-bucket and provider schema |
| `011_ActiveComplaintUniqueness.sql`–`017_CheckoutIdempotency.sql` | Incremental lifecycle constraints, financial idempotency and checkout changes |
| `018_AdminAuditEvents.sql` | Adds admin audit events |
| `019_RefundLedgerIdentity.sql` | Adds refund identity to ledger postings with uniqueness protection |
| `020_TransactionalOutbox.sql` | Adds/updates the Outbox table, lock token and polling index |
| `021_OutboxRetentionArchive.sql` | Adds the archive table for old Processed messages; does not move or delete existing rows |
| `Marketplace.Patch.ShippingCoverage.sql` | Incremental shipping-coverage changes |
| `FinancialConsistencyChecks.sql` | Read-only financial consistency diagnostics; **not a migration** |

For an existing database, first identify its actual schema/version and compare it with the migration preconditions. Back up the database, rehearse the upgrade on a restored copy, apply only the missing compatible migrations in the required order, then run the integration/financial checks. Do not blindly replay every patch against every database; not every historical script is guaranteed to be applicable to every intermediate schema.

The bootstrap and the historical migrations intentionally remain separate entry points. A single SQL file that is safe for both a truly empty database and every unknown historical schema cannot be achieved merely by concatenating scripts: duplicate objects, missing prerequisites, divergent column definitions, and non-idempotent changes can break deployment or risk data. A universal upgrade runner should only be introduced with explicit schema-version tracking, guarded migrations, precondition checks, and integration tests against each supported starting version. Until then, use the canonical bootstrap for new databases and the migration path for existing ones.

## Financial safety

- Never drop/recreate a populated database to apply the bootstrap.
- Never update or rebuild balances, `BalanceTransactions`, commissions, refunds, settlements, or ledger data as a side effect of health checks or schema diagnostics.
- `FinancialConsistencyChecks.sql` is for investigation and is read-only.
- Run production migrations under a controlled deployment window with a verified backup and a recorded schema version.
- Outbox health is diagnostic only. A warning is not authorization to mark a message `Processed` or edit a seller balance/ledger entry.

## Outbox delivery readiness

The dispatcher is disabled by default. Actual delivery requires the intended deployment configuration, including `Outbox__Enabled=true`, an HTTPS `Outbox__Webhook__Url`, and a strong `Outbox__Webhook__Secret`. Validate HMAC signature verification and receiver-side idempotency before enabling delivery in production. A persisted Outbox row does not mean the external receiver accepted the event.

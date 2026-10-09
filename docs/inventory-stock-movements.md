# Seller inventory management and low-stock overview

## Stock audit trail

Seller stock edits are recorded as immutable rows in `InventoryStockMovements`. Each row stores the variant, seller, previous total stock, new total stock, adjustment reason, and UTC timestamp. The signed delta is derived from the before/after quantities and is not independently editable.

- Stock history is read through `GET /api/sellers/me/variants/{variantId}/stock/movements?take=50`.
- The API checks that the variant belongs to a product in a store owned by the authenticated active seller before returning history.
- Stock changes use the existing stock endpoint and may include a `reason` (maximum 500 characters). A blank reason is replaced with a server-side generic reason for older clients.
- A history row is written only when total stock actually changes. Reserved quantity is not edited by this feature, and the existing invariant that stock cannot be set below reserved quantity remains enforced.
- Initial stock entered when creating a new variant is audited with the reason `موجودی اولیه تنوع`.
- This audit trail records seller-initiated stock adjustments; order reservations and consumption remain governed by the inventory reservation lifecycle.

## Thresholds and seller-wide dashboard

- Every inventory row has a `LowStockThreshold`; new rows default to 5 units.
- Low stock means available quantity is greater than zero and less than or equal to the configured threshold. A threshold of zero disables the low-stock state for positive quantities; zero available quantity is shown separately as out of stock.
- Threshold updates use `PUT /api/sellers/me/variants/{variantId}/stock/threshold` with `{ "threshold": 5 }`. The API validates a range of 0 through 1,000,000,000 and checks seller ownership.
- `GET /api/sellers/me/inventory/overview?filter=all&take=100` returns seller-wide counts for all variants, low-stock variants, and out-of-stock variants, plus a bounded item list. `filter` supports `all`, `low`, and `out`; `take` is clamped to 1–200.
- The seller catalog UI exposes the global overview across the seller's stores, filters, per-variant threshold editing, and a direct route to manage the selected product's stock.
- Low-stock SMS alerts are sent only when automatic SMS and low-stock SMS are enabled and a valid SMS provider is configured. An alert is de-duplicated while the variant remains in low-stock state and becomes eligible again after stock is replenished or the variant is no longer low.

## Database rollout

For an existing database, apply the SQL files in sequence when not already applied: `025_InventoryStockMovements.sql`, `026_InventoryLowStockThreshold.sql`, then `027_AutomaticSmsLowStockAlerts.sql`. Migration 026 is safe to re-run and aligns the inventory check constraint with the EF model. For a new empty database, use `database/Marketplace_Complete.sql`, which includes the inventory threshold, alert state, and audit table.

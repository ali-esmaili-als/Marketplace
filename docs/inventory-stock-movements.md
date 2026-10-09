# Seller inventory stock movement audit

Seller stock edits are recorded as immutable rows in `InventoryStockMovements`. Each row stores the variant, seller, previous total stock, new total stock, adjustment reason, and UTC timestamp. The signed delta is derived from the before/after quantities and is not independently editable.

- Stock history is read through `GET /api/sellers/me/variants/{variantId}/stock/movements?take=50`.
- The API checks that the variant belongs to a product in a store owned by the authenticated active seller before returning history.
- Stock changes use the existing stock endpoint and may include a `reason` (maximum 500 characters). A blank reason is replaced with a server-side generic reason for older clients.
- A history row is written only when total stock actually changes. Reserved quantity is not edited by this feature, and the existing invariant that stock cannot be set below reserved quantity remains enforced.
- Initial stock entered when creating a new variant is audited with the reason `موجودی اولیه تنوع`.
- This is an audit trail for seller-initiated adjustments, not a complete event stream of reservation/consumption operations. Order-driven stock lifecycle remains governed by inventory reservations.

For an existing database, apply `database/025_InventoryStockMovements.sql` once after the earlier schema patches. For a new empty database, use `database/Marketplace_Complete.sql` only; it includes this table, foreign keys, and indexes.

# Configurable low-stock threshold

## Behavior

- Every `InventoryItems` row stores `LowStockThreshold` as a non-negative `BIGINT`, defaulting to `5`.
- The allowed range is `0..1,000,000,000` in the domain model, API validation, and SQL check constraint.
- Low-stock is calculated from **available** quantity (`StockQuantity - ReservedQuantity`), not total stock. A positive available quantity at or below the threshold is low-stock. Zero available quantity is reported separately as out-of-stock.
- The seller can configure the threshold per product variant. The API checks that the variant's product belongs to a store owned by the authenticated active seller.
- Updating a threshold invokes the existing low-stock SMS notification flow; delivery still depends on SMS automation/provider settings and is not guaranteed by a successful API response.

## API

- `GET /api/sellers/me/variants/{variantId}/stock` returns stock, reserved, available, threshold, and low-stock state.
- `PUT /api/sellers/me/variants/{variantId}/stock/threshold` accepts `{ "threshold": 5 }`.
- `GET /api/sellers/me/inventory/overview?filter=all|low|out&take=100` returns seller-scoped inventory counts and rows.
- All seller inventory endpoints require `Seller.Catalog.Manage`.

## Database

- Fresh database: use `database/Marketplace_Complete.sql`; it defines the column, default, and check constraint.
- Existing database: apply `database/026_InventoryLowStockThreshold.sql` once. The patch is re-runnable and repairs the default constraint when the column already exists without one.
- Do not run incremental patches after creating a fresh database from the canonical complete script.

## Verification

The domain test suite includes threshold default/range behavior and checks that reservations affect low-stock calculations. Backend CI runs restore, Release build, and tests. Frontend CI currently runs the Angular build.

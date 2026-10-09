# Seller inventory replenishment thresholds

Each inventory item stores a `LowStockThreshold` (default: 5). The seller can configure a threshold from 0 to 1,000,000,000 for each product variant. The threshold compares against **available** quantity (`StockQuantity - ReservedQuantity`), not total physical stock.

- `GET /api/sellers/me/variants/{variantId}/stock` now returns `lowStockThreshold` and `isLowStock`.
- `PUT /api/sellers/me/variants/{variantId}/stock/threshold` accepts `{ "threshold": 5 }`.
- Both operations require `Seller.Catalog.Manage` and verify that the variant belongs to a store owned by the authenticated active seller.
- A threshold of zero disables the low-stock state for positive availability; zero available units remain out of stock.
- The seller catalog uses the configured threshold for its low-stock filter and summary; no automatic notification is sent by this feature.

Existing databases: apply `database/026_InventoryLowStockThreshold.sql` after `025_InventoryStockMovements.sql`. New databases: use `database/Marketplace_Complete.sql`.

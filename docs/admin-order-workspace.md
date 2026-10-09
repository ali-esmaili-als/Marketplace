# Admin order operations workspace

The read-only order investigation API is available to users granted the dedicated `Admin.Order.Read` rule.

## Endpoints

- `GET /api/admin/orders`: paginated newest-first search. Optional query parameters: `status` (1–10), `orderId`, `sellerId`, `storeId`, `fromUtc`, `toUtc`, `page`, and `pageSize` (capped at 100).
- `GET /api/admin/orders/{orderId}`: order snapshot, line-item snapshots, shipping destination and the latest payment status/reference.

The list includes status counts for the selected order/seller/store/date filters, independent of the selected status filter. All endpoints are read-only and use `AsNoTracking`; they do not mutate order status, inventory reservations, refunds, settlements, seller balances, or ledger entries.

## UI

The Angular route `/admin/orders` provides status overview tiles, date and ID filters, pagination, and an order detail panel. The screen is investigative only: financial processing and lifecycle mutations remain in their dedicated workflows.

## Deployment

- New databases: use `database/Marketplace_Complete.sql`, which seeds `Admin.Order.Read`.
- Existing databases: apply `database/022_AdminOrderReadPermission.sql`.
- Grant the new rule to the intended admin users through the identity/rule administration workflow. Existing permissions are not automatically broadened.
- Deploy the API and frontend changes together. A frontend route guard is a usability boundary; the API permission handler remains the security boundary.

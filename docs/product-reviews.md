# Product reviews and moderation

- Customers can submit one review per product only when the product appears in one of their Delivered or Completed orders.
- A review captures the originating order, a 1–5 rating, title, body, and submission timestamp.
- New reviews start Pending and are not visible publicly until an admin approves them.
- Rejected reviews require a moderation note. Only pending reviews can be moderated.
- Public product pages show approved reviews and the average rating calculated from the displayed approved set.
- The unique database constraint on (CustomerId, ProductId) prevents duplicate reviews even under concurrent submissions.

## API
- `GET /api/public/products/{productId}/reviews`
- `GET /api/products/{productId}/reviewable-orders` (authenticated; requires `Order.ReadOwn`)
- `POST /api/products/{productId}/reviews` (authenticated; requires `Order.ReadOwn`)
- `GET /api/admin/product-reviews?status=1&take=100` (requires `Admin.Order.Read`)
- `PUT /api/admin/product-reviews/{reviewId}/moderation` (requires `Admin.Order.Read`)

Run `database/028_ProductReviews.sql` after the previous database patches. Fresh databases should use the updated `database/Marketplace_Complete.sql`.

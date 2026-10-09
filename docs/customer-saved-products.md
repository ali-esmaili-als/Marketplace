# Customer saved products

## Scope
Customers can save active catalog products to a personal wishlist and remove them later. This is a convenience feature only: the saved row does not snapshot price, discount, stock, or warranty. The product detail/catalog API remains authoritative for current price and availability.

## API
- `GET /api/me/saved-products`: returns the authenticated customer's saved active products, newest first, limited to 200.
- `POST /api/me/saved-products/{productId}`: saves an active product; repeat requests are idempotent when the row already exists.
- `DELETE /api/me/saved-products/{productId}`: removes the current customer's saved row; deleting a missing row succeeds.
- All endpoints require `Order.ReadOwn` and derive customer identity from the authenticated principal. No customer ID is accepted from the request body.
- A unique database constraint on `(CustomerId, ProductId)` prevents duplicate saves under concurrent requests.

## Database
- Fresh database: `database/Marketplace_Complete.sql` includes `dbo.SavedProducts`.
- Existing database: apply `database/029_CustomerSavedProducts.sql`.
- Rows cascade-delete with their user or product. The UI must handle inactive/deleted products and always re-read catalog data before purchase.

## Verification
Domain tests cover identifier invariants. CI is responsible for compiling the API/domain and running the test suite. No deployment is implied by a green build.

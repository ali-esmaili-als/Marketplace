# Persistent storefront themes

- Stores persist one of three validated theme codes: `classic`, `minimal`, or `vibrant`.
- Sellers configure the selected store theme in the seller catalog page. The authenticated API verifies store ownership and permission.
- Public store, product-detail, cart-summary, and checkout responses include the theme context. Store home, category, product detail, cart, and checkout pages apply it from API data and path-based store routing; theme state is not carried in query strings.
- Existing stores receive `classic` as the default. Apply `database/031_StoreThemes.sql` to existing databases, or use the updated `database/Marketplace_Complete.sql` for a new database.

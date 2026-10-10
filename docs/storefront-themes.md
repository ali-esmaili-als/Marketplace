# Storefront layouts and color palettes

A store persists its presentation settings on the store record. Public storefront, category, product-detail, cart-summary, and checkout APIs return the saved theme and palette; the client must not use query-string parameters to choose presentation.

## Layouts

The seller can select one of 20 distinct layout treatments:

- `classic`
- `minimal`
- `vibrant`
- `editorial`
- `boutique`
- `magazine`
- `grid`
- `luxe`
- `organic`
- `tech`
- `fashion`
- `gallery`
- `market`
- `mono`
- `pastel`
- `bold`
- `nordic`
- `artisan`
- `urban`
- `elegant`

## Palettes

Every layout can be combined with one of five palettes:

- `ocean` — blue/teal
- `forest` — green
- `sunset` — amber/coral
- `rose` — rose/magenta
- `monochrome` — neutral grayscale

The server validates both codes, the database has matching check constraints, and `database/032_StoreLayoutsAndPalettes.sql` upgrades existing databases. The canonical `database/Marketplace_Complete.sql` includes both columns and constraints for new databases.

Default values remain backward-compatible: `classic` layout and `ocean` palette.


## Custom appearance

Each store persists its own layout, palette, primary/secondary/background/text colors, font preset, and corner style. Sellers can update only stores owned by their seller profile through `PUT /api/sellers/me/stores/{storeId}/theme`. Administrators with `Admin.Identity.Manage` can assign or override the same appearance through `PUT /api/admin/stores/{storeId}/theme`; both APIs share domain validation for HEX colors and the supported font/corner presets. Public store/product and authenticated cart summaries return the persisted appearance for the customer UI.

## Storefront media

Store logo, banner, and product-gallery images use local application storage rather than remote placeholder URLs. Upload bytes are stored under `wwwroot/uploads/storefront/{storeId}/` and the `StorefrontMedia` table stores the public URL and ownership metadata.

- Public listing: `GET /api/public/stores/{storeId}/media`
- Seller upload: `POST /api/sellers/me/stores/{storeId}/media?kind=logo|banner|product[&productId=...]`
- Seller hide: `DELETE /api/sellers/me/stores/{storeId}/media/{mediaId}`
- Admin upload/hide: `POST /api/admin/stores/{storeId}/media` and `DELETE /api/admin/stores/{storeId}/media/{mediaId}`

Uploads accept JPEG, PNG, or WebP only, up to 5 MiB. The API validates both declared MIME type and file signature, generates opaque filenames, and verifies store ownership for seller operations. A product image must reference a product belonging to the same store. Deactivation hides media from the public API and removes the physical file so the direct static URL no longer serves the image.

Existing database upgrade: run `database/034_StorefrontMedia.sql` after `033_StoreThemeCustomization.sql` (and after earlier numbered upgrades). A fresh database uses `database/Marketplace_Complete.sql`.

For production, persist and back up `wwwroot/uploads` on durable storage and share it across API instances; ephemeral container storage is not suitable for uploaded media. The upload directory and SQL metadata must be backed up together.

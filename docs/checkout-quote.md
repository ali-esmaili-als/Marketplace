# Server-calculated checkout quote

## Endpoint

Authenticated customers with `Order.Create` can request:

`GET /api/orders/checkout/quote?destinationCityId={id}&couponCode={optional-code}`

The quote is read-only. It does not create an order/payment, reserve stock, consume a coupon, or change seller balances. It returns store and destination snapshots, line prices, warranty price, campaign/coupon discount breakdown, currently available stock, total payable, and quote timestamp.

## Validation

The quote uses the same `PricingService` as final checkout and validates:
- cart belongs to one active store and its seller;
- destination is active and the store is configured to ship to it;
- every cart product/variant is active and belongs to the expected store/product;
- requested quantities are currently available;
- selected warranties are active;
- campaign/coupon compatibility and coupon rules, scope, caps and usage eligibility.

The checkout command remains authoritative and repeats validation in its serializable transaction immediately before reserving stock and creating the order. A quote is informational and can become stale if inventory, campaign or coupon usage changes.

## Price semantics

Subtotal includes merchandise and selected warranty prices. Campaign discounts apply to eligible merchandise; warranty prices are not discounted. Coupon discounts use the existing pricing rules and maximum-discount cap. The current schema has store-to-city shipping coverage but no configured shipping-price/rate model, so the quote explicitly does not add a shipping charge. No new database migration is required.

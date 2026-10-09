# Shipping rates, shipment registration and tracking

## Seller shipping configuration
Each store configures its delivery coverage and exactly one shipping rate per enabled destination city. A rate stores the shipping fee in IRR plus minimum and maximum estimated delivery days. Rates must be non-negative; estimates must be between 0 and 365 days with maximum greater than or equal to minimum.
Existing coverage is backfilled by the migration with a zero shipping fee and a 3–7 day estimate to preserve checkout compatibility. Sellers should review those defaults before taking real orders.

## Checkout and financial invariants
The server's checkout quote and checkout operation both read the same store/city rate. The fee is added after product campaign/coupon discounts, included in the payment amount, and snapshotted in Orders.ShippingFeeIRR. Later rate edits do not change existing orders.
Commission is calculated against the discounted merchandise amount, excluding the shipping fee. The shipping fee remains part of seller proceeds, so Commission.SellerAmountIRR and Order.SellerAmountIRR continue to reconcile. Existing ledger, hold and settlement transitions remain unchanged.

## Carrier tracking scope
Carrier shipment tracking is separate from the platform's delivery confirmation (Deliveries and DeliveryCodes). A carrier-reported delivery is informational only; it does not mark an order as delivered in the marketplace, end the complaint window, move seller balances, or release a balance hold.

## Seller endpoints
- POST /api/seller/orders/{orderId}/shipment registers one carrier, tracking number and optional HTTP(S) tracking URL for an eligible paid order. A unique database constraint prevents a second shipment for the same order.
- POST /api/seller/orders/{orderId}/shipment/status records a validated status transition, description, optional location and optional event time.
- GET /api/seller/orders/{orderId}/shipment returns the shipment and ordered tracking events for the owning seller.
- GET /api/stores/{storeId}/shipping-rates and PUT /api/stores/{storeId}/shipping-rates read and save city-specific fees and delivery estimates for the owning seller.
Shipment endpoints verify seller ownership and use existing permissions. Rate configuration requires Seller.Shipping.Configure.

## Customer endpoint
- GET /api/orders/{orderId}/shipment returns tracking information only when the authenticated user owns the order.

## Status transitions
| Current | Allowed next statuses |
| --- | --- |
| Registered | Shipped, Cancelled |
| Shipped | InTransit, OutForDelivery, Exception, Returned |
| InTransit | OutForDelivery, Exception, Returned |
| OutForDelivery | CarrierDelivered, Exception, Returned |
| Exception | InTransit, OutForDelivery, Returned |
| CarrierDelivered, Returned, Cancelled | Terminal |
Event timestamps cannot be in the future or precede the previous event. Status updates and their in-app notifications are saved in the same database transaction.

## Database
- New-install schema: database/Marketplace_Complete.sql
- Existing database upgrades: run database/023_ShipmentTracking.sql and database/024_StoreShippingRates.sql in order.
- Shipment and rate IDs use the application's ID generator.

## Operational note
Statuses are seller-entered at this stage. No carrier API integration or proof-of-delivery webhook is claimed. Do not enter the buyer's delivery code in tracking descriptions.
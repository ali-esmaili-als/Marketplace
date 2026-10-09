# Order notification lifecycle

## Delivery model

In-app notifications are persisted in `dbo.Notifications`. Order lifecycle notifications are added inside the same unit-of-work transaction as the order, delivery, complaint, and related inventory/financial lifecycle change. If the business transaction rolls back, its notifications roll back as well. They do not trigger or alter Ledger, seller balances, holds, commissions, inventory, refunds, or settlements.

The existing Transactional Outbox remains responsible for outbound webhook delivery. In-app notification creation is not coupled to webhook availability and does not require the outbound webhook dispatcher to be enabled.

## Events currently notified

- Payment succeeds: customer receives a payment confirmation; seller receives a new paid-order alert.
- Payment window expires: customer and seller receive cancellation/expiry notices.
- Seller confirms delivery: customer and seller receive delivery confirmation.
- Delivery deadline expires: customer and seller receive an expiry notice.
- Customer opens a complaint: seller receives a complaint alert.
- Admin resolves a complaint: both customer and seller receive the result.
- Complaint window closes and the order completes: both customer and seller receive completion notices.
- Seller marks an order ready: customer receives the delivery code. The code remains delivered through the existing authenticated in-app notification flow.

The notification recipient is always a user ID. Seller notifications resolve the seller's owning user through the Sellers table; no seller ID is treated as a Users ID.

## API

All endpoints require authentication and are scoped to the current user's identity:

- `GET /api/notifications?take=50` — latest notifications; `take` is clamped to 1–100.
- `GET /api/notifications/unread-count` — returns `{ "count": number }`; unread means `ReadAtUtc IS NULL`.
- `POST /api/notifications/{notificationId}/read` — marks only a notification belonging to the current user as read.
- `POST /api/notifications/read-all` — marks all unread notifications belonging to the current user as read and returns `{ "updated": number }`.

No schema migration is needed for this feature; it uses the existing Notifications table and its read timestamp. `database/Marketplace_Complete.sql` remains the canonical fresh-install schema. Existing notification rows are preserved.

## Frontend

The customer inbox is available at `/notifications`; the same user-scoped inbox is available to sellers at `/seller/notifications`. Both show unread count, individual read action, read-all action, and order navigation adapted to the active workspace. API authorization remains authoritative; the route guard is not used as a substitute for server-side identity scoping.

## Operational notes

- Deploy Backend and Frontend together so the inbox's unread-count and read-all calls are supported.
- The notification list is currently capped at the latest 100 items in the UI; the API supports smaller or larger bounded `take` values.
- Notifications are in-app only. This feature does not claim SMS or email delivery.

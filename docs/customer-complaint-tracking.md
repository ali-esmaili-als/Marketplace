# Customer complaint tracking

## Customer workflow
- Customers can open a complaint from the details of an eligible delivered order.
- The customer tracking page at `/complaints` lists only complaints owned by the authenticated customer, with order amount, status, creation time, and resolution outcome.
- Open or under-review complaints can be cancelled by their owner. Resolved and closed complaints cannot be cancelled.
- The endpoint does not accept a customer ID from the caller. Ownership is derived from the authenticated principal.

## API
- `GET /api/me/complaints?take=100`: returns the current customer's latest complaints; `take` is clamped to 1–200.
- `POST /api/me/complaints/{complaintId}/cancel`: cancels an unresolved complaint owned by the current customer; returns 404 for a missing or foreign complaint.
- Listing requires `Order.ReadOwn`; cancellation requires `Order.Create`.
- Complaint creation remains `POST /api/orders/{orderId}/complaints` and uses the existing order-ownership and complaint-window domain checks.

## Data and verification
No schema change is needed; the workflow uses the existing `dbo.Complaints` table and domain state transitions. Domain tests cover cancellation, rejection of cancellation after resolution, and the requirement to start review before resolving a complaint. CI validates compilation and tests; a green CI run does not imply deployment.

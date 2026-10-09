# Payment verification outcome contract

Payment provider verification has three semantic outcomes even though the adapter returns a compact result:

- **Confirmed success**: `IsSuccessful=true` and `IsOutcomeDefinitive=true`. The application proceeds through the serializable payment/order/ledger finalization path.
- **Confirmed failure**: `IsSuccessful=false` and `IsOutcomeDefinitive=true`. The application may mark a still-pending/redirected payment and its initiated transaction as failed.
- **Unknown / pending**: `IsOutcomeDefinitive=false`, regardless of `IsSuccessful`. The application returns `OutcomeUnknown=true` and leaves payment, provider transaction, order, reservations, seller balance, and ledger untouched.

A transport timeout/exception also remains unknown and propagates without changing payment state. Adapters must not classify a pending response, malformed response, transient provider error, or ambiguous timeout as a confirmed rejection. If a provider protocol cannot prove final failure, return an indeterminate result and allow the customer to refresh the existing order/payment status instead of starting a replacement payment.

This contract does not implement a real bank protocol. The built-in bank adapters remain unconfigured until their official request/response and signature validation protocols are implemented and tested in the provider sandbox.

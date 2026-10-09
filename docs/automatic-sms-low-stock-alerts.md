# Automatic SMS and low-stock alerts

## Admin controls
The Admin > SMS providers page exposes two persisted switches:
- **Automatic SMS**: master switch for system-generated SMS messages. It defaults to off.
- **Low-stock SMS**: sends a stock warning to the seller's registered mobile. It can only be enabled when Automatic SMS is on.

The existing OTP login SMS selection is intentionally separate from the automatic notification switch. Provider credentials are not stored in the database.

## Low-stock behavior
Each inventory variant has a configurable `LowStockThreshold` (default 5). A warning is eligible when available stock (`StockQuantity - ReservedQuantity`) is greater than zero and less than or equal to the threshold. The SMS includes the store, product, variant, available quantity, and configured threshold.

After a successful send, the alert is marked sent for the current low-stock episode to avoid repeated messages on every stock edit. Once available stock rises above the threshold, the alert state resets so a later drop can generate a new warning. A copy of the SMS and its delivery status is stored in the user's notification feed under `LowStock`.

## Provider configuration
Automatic text messages require a provider selected under Admin > SMS providers and provider-specific sending credentials:
- Kavenegar: `Notifications:Sms:Kavenegar:Sender` (or existing OTP sender configuration).
- SMS.ir: `Notifications:Sms:SmsIr:LineNumber`; API key can be supplied under `Notifications:Sms:SmsIr:ApiKey` or reused from OTP settings.
- Melipayamak: `Notifications:Sms:Melipayamak:From` (or existing OTP sender configuration).
- HTTP API: `Notifications:Sms:HttpApi:Endpoint` and optional `Notifications:Sms:HttpApi:ApiKey`.

Configure and verify these settings with the chosen provider before enabling automatic SMS in production. The Test provider deliberately performs no external delivery and is intended for non-production checks.

## Database
Run `database/027_AutomaticSmsLowStockAlerts.sql` after migrations 025 and 026. New installs should use the updated `database/Marketplace_Complete.sql` script.

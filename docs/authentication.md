# Authentication and SMS OTP configuration

The password login endpoint remains `POST /api/auth/login` and uses the user's mobile number as the username. Customer registration is `POST /api/auth/register/customer`.

OTP capability is exposed by `GET /api/auth/options`. The response contains `otpEnabled`; the Angular login screen hides the OTP option when it is false. The API also rejects OTP requests and verification when no registered provider is selected.

## Provider selection

Set `Authentication:Otp:Provider` in environment-specific configuration or environment variables:

- Empty string: OTP disabled; password login and registration remain available.
- `Test`: no SMS is sent; the OTP is always `1234` and expires after five minutes. Use only in local/test environments.
- `HttpApi`: sends an HTTPS JSON POST to `Authentication:Otp:HttpApi:Endpoint` with `{ "mobile": "...", "message": "..." }`. If configured, the API key is sent in the `X-Api-Key` header. This is a generic adapter contract, not a vendor-specific integration; adapt the payload/authentication to the chosen SMS vendor.

Example environment variables:

```text
Authentication__Otp__Provider=Test
Authentication__Otp__HttpApi__Endpoint=
Authentication__Otp__HttpApi__ApiKey=
```

For production, add a vendor-specific implementation of `ISmsProvider`, register it in `Marketplace.Api/Program.cs`, and set `Authentication:Otp:Provider` to its `Name`. Provider implementations should keep credentials in secret/environment configuration, require HTTPS, and never log OTP values.

## Current limitations

OTP challenges are stored in process memory. This is suitable for local development and a single-instance test deployment, but production multi-instance deployments should replace the in-memory challenge store with a shared persistent/distributed store and apply provider-specific rate limits. The test provider's fixed code must never be enabled in production.

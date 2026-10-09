# Payment provider secrets

Payment-provider configuration may use environment-backed references instead of storing credential values in SQL Server.

For a sensitive JSON property, use `env:VARIABLE_NAME` rather than the real credential value:

```json
{
  "CreateUrl": "",
  "VerifyUrl": "",
  "RefundUrl": "",
  "CallbackUrl": "",
  "MerchantId": "merchant-id",
  "TerminalId": "terminal-id",
  "Username": "merchant-user",
  "Password": "env:PAYMENT_MELLI_PASSWORD",
  "AdditionalJson": null
}
```

Define the referenced variable in the API host environment (or a managed secret provider exposed through .NET configuration), e.g. `PAYMENT_MELLI_PASSWORD`. Never commit real values to source control or place them in SQL scripts, audit events, or support tickets.

At runtime the API resolves `env:` references in memory immediately before creating a gateway adapter. The database retains the reference, and the admin API continues to redact sensitive fields. If the referenced secret is missing, gateway creation fails closed. Restrict access to environment configuration and process diagnostics.

**Important:** the currently supported operational gateway remains the test gateway outside Production. Real bank adapters still require their official protocols and certification; this feature establishes safer credential configuration for future integrations.

# Customer profile API

- `GET /api/me/profile` returns the authenticated user's ID, mobile, email, display name, and mobile-verification state.
- `PUT /api/me/profile` accepts `displayName` and optional `email`; the user ID is derived from the validated JWT, never from the request body.
- Display name is required and limited to 120 characters. Email is optional and limited to 254 characters.
- Mobile cannot be changed through this endpoint. Email is a contact field and is not marked verified.
- This change does not alter schema or financial entities. The endpoint requires authentication.

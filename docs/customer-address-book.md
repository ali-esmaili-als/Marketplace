# Customer address book

- Authenticated customers manage their own delivery addresses through `/api/me/addresses`.
- Each address has a delivery city, recipient name/mobile, street address, postal code, optional note, and default flag.
- At most one address per customer is default; deleting the default address promotes the most recently updated remaining address.
- Checkout references an address ID, but the API verifies that the address belongs to the current customer and that its city matches the selected shipping city.
- Orders snapshot recipient and address fields at creation so later address edits do not rewrite historical delivery details.
- Apply `database/030_CustomerAddressesAndOrderAddress.sql` before deploying the API.

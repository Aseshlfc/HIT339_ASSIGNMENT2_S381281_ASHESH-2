# Public Catalogue API (v1)

A secure, **read-only** JSON API for the library catalogue, categories and operating status.

Base address (local run): `http://localhost:5190/api/v1` (use the port shown when you run `dotnet run`).

## Authentication
Send your key in the `X-Api-Key` header on every request. Keys in the URL are not accepted, because URLs end up in logs and browser history.

```bash
curl -H "X-Api-Key: <your-api-key>" http://localhost:5190/api/v1/status
```

Replace `<your-api-key>` with a key configured under `PublicApi:ApiKeys`. The key that ships in `appsettings.json` is for development only. To change keys, edit `appsettings.json`:

```json
"PublicApi": {
  "ApiKeys": ["a-long-random-key", "another-key-for-a-second-client"],
  "RequestsPerMinute": 60,
  "DefaultPageSize": 20,
  "MaxPageSize": 100
}
```

The same settings can be set with environment variables, for example `PublicApi__ApiKeys__0=...`. If no keys are configured the API answers 503 ("switched off").

## Endpoints
| Method | Path | What it returns |
|---|---|---|
| GET | `/items` | A page of catalogue items |
| GET | `/items/{libraryCode}` | One item (code is not case sensitive) |
| GET | `/categories` | Item types with counts per genre / toy type |
| GET | `/status` | Operating status and stock totals per branch |

### GET /items
| Query parameter | Meaning |
|---|---|
| `q` | Text found in the name, description or library code |
| `type` | `Book`, `Music` or `Toy` |
| `category` | Genre (books, music) or toy type (toys), as listed by `/categories` |
| `branch` | Branch code, for example `CEN`, `NTH`, `RIV` |
| `status` | `Available`, `Borrowed`, `Damaged` or `InTransit` |
| `availableOnly` | `true` to show only items that can be borrowed now |
| `page` | Page number, from 1 (default 1) |
| `pageSize` | 1 to 100 (default 20) |

Filters combine with AND. Results are sorted by name, then library code. Destroyed items are never included.

```bash
curl -H "X-Api-Key: <your-api-key>" \
  "http://localhost:5190/api/v1/items?type=toy&availableOnly=true&pageSize=2"
```

```json
{
  "page": 1, "pageSize": 2, "totalItems": 6, "totalPages": 3,
  "items": [
    {
      "libraryCode": "T002",
      "name": "Jigsaw Puzzle 500pc",
      "description": "A challenging 500-piece landscape jigsaw puzzle.",
      "type": "Toy",
      "category": "Puzzle",
      "status": "Available",
      "isAvailable": true,
      "branch": { "code": "NTH", "name": "Northside Branch" },
      "toyType": "Puzzle",
      "minimumAge": 8,
      "manufacturer": "Library Toy Collection"
    }
  ]
}
```

Fields that only apply to one type are left out for the other types: books have `author`, `genre`, `year`; music has `artist`, `genre`, `year`; toys have `toyType`, `minimumAge`, `manufacturer`.

### GET /items/{libraryCode}
```bash
curl -H "X-Api-Key: <your-api-key>" http://localhost:5190/api/v1/items/T005
```
Returns one item in the shape above, or 404 if the code does not exist.

### GET /categories
Returns one entry per type, each with `itemCount`, `availableCount` and a `categories` list of `{ "name", "itemCount" }`.

### GET /status
```json
{
  "status": "operational",
  "apiVersion": "1.0",
  "serverTimeUtc": "2026-10-13T07:42:13Z",
  "databaseReachable": true,
  "totals": { "items": 26, "available": 19, "borrowed": 4, "damaged": 3, "inTransit": 0 },
  "branches": [
    { "code": "CEN", "name": "Central Library", "address": "1 Library Square, Springfield",
      "phone": "0800000001", "isActive": true, "itemCount": 12, "availableCount": 9 }
  ]
}
```
If the database cannot be reached the response is HTTP 503 with `"status": "degraded"`, so a monitor can alert on it.

## Errors
Errors use the standard problem format (`application/problem+json`):

```json
{ "title": "Bad request", "status": 400, "detail": "type must be Book, Music or Toy." }
```

| Status | When |
|---|---|
| 400 | A parameter is invalid (bad `type`, `status`, `page` or `pageSize`) |
| 401 | Missing or wrong API key (includes `WWW-Authenticate: ApiKey`) |
| 404 | Unknown library code or branch code |
| 405 | Any method other than GET |
| 429 | Too many requests; wait the number of seconds in `Retry-After` |
| 503 | No keys configured, or the database is unreachable (`/status`) |

## Security notes
- Read-only: nothing in the API can create, change or delete data.
- Keys are compared in constant time (SHA-256 hash, then fixed-time comparison) and only read from the header.
- Per-client-address rate limit (default 60 requests per minute).
- No internal database ids, borrower details or loan history are returned.
- No CORS headers are sent, so websites cannot call the API directly from a visitor's browser; call it from a server.
- Keep keys out of source control for any real deployment (use environment variables or a secrets store).

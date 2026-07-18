# People Explorer

A C# console application that browses, searches, and inspects people from the public
TripPin OData v4 sample service (`https://services.odata.org/TripPinRESTierService/`,
entity set `People`). Built to demonstrate a clean, testable architecture and
production-minded engineering (defensive query construction, structured logging with
PII protection, graceful failure handling, unit test coverage) rather than to showcase
every pattern available — see [Design decisions](#design-decisions).

## Features

| Feature | Notes |
|---|---|
| List people, paged | `$top`/`$skip`/`$count`, stable `$orderby=UserName` so pages don't overlap or skip |
| Person details | All simple fields, `AddressInfo`, `Trips` via `$expand`; 404 → friendly message, not a crash |
| Search / filter by field | Server-side `$filter=contains(...)`, case-insensitive |
| Graceful failure handling | Every OData/transport failure surfaces as one friendly message — see [Error handling](#error-handling) |
| Efficient pagination | One HTTP round-trip per page turn, not two — see [Pagination](#pagination) |
| PII-safe logging | Masking is fail-closed: a new field can't slip into the logs unmasked by default — see [Logging](#logging) |
| Test coverage | 35 xUnit tests, all offline (business logic, OData query construction, log masking) |

## Getting started

**Prerequisites:** .NET 10 SDK.

```bash
dotnet build
dotnet run --project Client.Presentation
```

The app talks to the live TripPin service over the network — no server setup needed.
Configuration lives in `Client.Presentation/appsettings.json` (`Client:ServiceUrl`,
`Client:PageSize`, default page size 10). Both are validated at startup — a missing
`ServiceUrl` or a non-positive `PageSize` fails fast with a `Configuration error: ...`
message instead of starting in a broken state.

### Menu

```
1) List people (paged)   -> [n]ext / [p]revious / a page number / [q] back
2) Search people         -> pick a field, then a search term
3) Person details        -> enter a UserName, e.g. russellwhyte
0) Exit
```

### Running the tests

```bash
dotnet test Client.Tests
```

## Architecture

Four assemblies plus a test project, following Onion/Clean Architecture layering
(Domain → Application → Infrastructure/Presentation); dependencies flow one way:

```
                     Client.Presentation
                       /            \
                      v              v
       Client.Infrastructure    Client.Application
                      \              /
                       v            v
                      Client.Core (Domain)

Client.Tests  ---->  everything (Core, Application, Infrastructure)  -  still offline,
                     Infrastructure is tested against a fake HTTP transport, never
                     the real network
```

- **Client.Core** — the domain, innermost ring. No external package references at all.
  - `Models/` — `Person`, `Location`, `City`, `Trip`, `PagedResult<T>` (the
    application-facing page, with `Page`/`TotalPages`), `PeoplePage` (the
    gateway-facing page, with just `Items`/`TotalCount` — see
    [Pagination](#pagination)).
  - `Abstractions/` — `IClientGateway` (data access port, implemented by
    Infrastructure) and `IClientService` (the application-service port,
    implemented by Application). Both interfaces live here, one ring below either
    implementation, so Infrastructure and Application never need to reference each
    other — each depends only on Core.
  - `Exceptions/ClientGatewayException.cs` — the one failure type `IClientGateway`
    is allowed to throw; see [Error handling](#error-handling).
- **Client.Application** — the use-case ring, depends only on Core.
  - `ClientService.cs` — business logic only: pagination (page clamping,
    `TotalPages` calculation), input validation for search/details. No I/O; it only
    calls `IClientGateway`. This is what the xUnit tests exercise, against a fake
    gateway, no network involved.
  - `ClientOptions.cs` — bound from `appsettings.json`'s `Client` section
    (`ServiceUrl`, `PageSize`); `PageSize` is consumed directly by `ClientService`.
- **Client.Infrastructure** — implementation details, depends on Core (not on
  Application — a sibling ring, same depth).
  - `ClientGateway.cs` — talks to TripPin via `PanoramicData.OData.Client`. All
    OData query building (`$filter`/`$orderby`/`$top`/`$skip`/`$expand`/`$count`)
    lives here; nothing above this layer sees OData syntax. Every OData-specific
    failure is caught and re-thrown as `ClientGatewayException`. `GetPeopleAsync`
    fetches a page and its total count in one request (see
    [Pagination](#pagination)).
  - `LoggingClientGateway.cs` — a decorator over `IClientGateway` that adds
    structured logging. Kept separate from `ClientGateway` so the data-access code
    stays free of cross-cutting concerns.
- **Client.Presentation** — composition root, outermost ring; the only project that
  references both Application and Infrastructure.
  - `Program.cs` — reads configuration, sets up Serilog, wires DI (`ClientGateway`
    wrapped by `LoggingClientGateway`, exposed as `IClientGateway`; `ClientService`
    exposed as `IClientService`), runs the menu.
  - `UI/ConsoleMenu.cs` — the console menu (List / Search / Details), talks only to
    `IClientService`.
  - `Logging/PersonDestructuringPolicy.cs` — controls how a `Person` is rendered in
    logs; see [Logging](#logging).
- **Client.Tests** — xUnit. References Core, Application, and (for `ClientGateway`
  coverage) Infrastructure — but never touches the real network; the network
  boundary is faked, not skipped.
  - `FakeClientGateway.cs` — in-memory `IClientGateway` (mirrors the server's
    stable-order/contains-search semantics closely enough to test against) — used
    by `ClientServiceTests.cs`.
  - `ClientServiceTests.cs` — pagination (clamping above/below range, `TotalPages`
    ceiling-division, empty source → "page 1 of 1", one gateway call for an
    in-range page vs. two for an out-of-range one), details (not found → `null`,
    blank username → `ArgumentException`), search (blank term → `ArgumentException`,
    field-scoped matching).
  - `RecordingHttpMessageHandler.cs` — a fake `HttpMessageHandler` fed straight into
    `ODataClientOptions.HttpClient`, so `ClientGateway` runs its real code against a
    scripted transport instead of a hand-rolled test double of the OData client
    itself — used by `ClientGatewayTests.cs`.
  - `ClientGatewayTests.cs` — asserts the actual query strings `ClientGateway`
    builds ($orderby/$skip/$top, `$count=true` riding on the same request as the
    page, the $filter shape per `SearchField`, $expand=Trips toggling,
    case-insensitive search), that 404 → `null`, and that a 500 →
    `ClientGatewayException`.
  - `PersonDestructuringPolicyTests.cs` — asserts that no personal field value ever
    reaches a rendered log line, regardless of which property on `Person` it came
    from.

## Design decisions

- **PanoramicData.OData.Client** as the OData client — chosen for native `ILogger`
  integration, built-in retry, typed exceptions (`ODataNotFoundException`), and
  `System.Text.Json` under the hood. Query methods are expression-based
  (`.Filter(p => p.FirstName.ToLower().Contains(term))`), not string concatenation,
  so a search term can't break out into arbitrary OData syntax.
- **Case-insensitive search.** OData's `contains()` is case-sensitive by default;
  the gateway lower-cases both the property and the search term
  (`p.FirstName.ToLower().Contains(term.ToLowerInvariant())`, which the client
  library translates to `contains(tolower(FirstName), '...')`) so a search box that
  only matches exact casing doesn't feel broken.
- **xUnit** for tests, business logic tested fully offline via `FakeClientGateway`,
  and `ClientGateway`'s OData query construction tested offline via a fake
  `HttpMessageHandler` (`RecordingHttpMessageHandler`) — no network access from the
  test suite, for either.
- **Deliberately simple.** No MediatR-style handlers, no Result pattern, no CQRS
  infrastructure, no Docker. This is a take-home exercise, not a production system.

### Error handling

`ClientGateway` translates every OData/transport failure (auth, server errors,
network issues the client library's own retries didn't recover from) into a single
domain-defined `ClientGatewayException` — except "not found", which stays a `null`
return from `GetPersonAsync`, since callers treat it as a normal outcome, not a
failure. `ConsoleMenu` catches the exception and shows a friendly message instead
of crashing the whole session; `LoggingClientGateway` also logs a Warning (visible
on the console) for every such failure. Presentation never needs to reference
`PanoramicData.OData.Client.Exceptions` at all — swapping the OData client library
for something else would only touch `Client.Infrastructure`.

### Pagination

`GetPeopleAsync` asks for `$count=true` on the same request as `$top`/`$skip`
(`PeoplePage.TotalCount` comes back alongside `PeoplePage.Items`), instead of a
separate count request. `ClientService.GetPageAsync` requests the page the caller
asked for directly — the lower bound (page ≥ 1) doesn't need the total count to
enforce, and the upper bound only becomes known once that first response comes
back. If the requested page turns out to be within range (every next/prev/first
navigation always is), that's the whole operation: **one** HTTP round-trip. Only a
request for a page past the last one (e.g. the user typing a page number beyond the
end) needs a second, corrective re-fetch with the clamped page — **two**
round-trips, and only in that case. Two options were on the table (merge count into
the page request vs. cache the total count across page turns); the merge was
chosen because it has no staleness/cache-invalidation question attached to it — the
count is still fetched live, every time, just on one request instead of two.

Two edge cases that fall out of the design above:

- **Overflow.** A page number can be typed in far larger than the data could ever
  support (e.g. `2000000000`). It's clamped to the largest page whose `skip` still
  fits in an `int` before that first request goes out, so `(page - 1) * PageSize`
  can't overflow; `checked` arithmetic backstops that clamp.
- **Snapshot consistency.** On the corrective second request, `TotalPages`/
  `TotalCount` are (re)computed from that response rather than reused from the
  first, so they can't describe two different snapshots if the backing data
  changed between the two calls. `Page` still reflects the window that was
  actually fetched (what `Items` came from), which in that same rare case can read
  as e.g. "page 3 of 2" — a stale-but-honest label, not a hidden inconsistency.

### Logging

Console shows Warning+ only (so it doesn't clutter the menu); the full Information
trail goes to `logs/people-explorer-<date>.log` (rolling daily, 7 days retained).
Personal fields (`Emails`, `FirstName`, `LastName`, `MiddleName`, plus the computed
`FullName`, which otherwise carries the same PII under a different name) are masked
wherever a `Person` is logged as a structured object (`{@Person}`), via a custom
`IDestructuringPolicy` (`PersonDestructuringPolicy`). It builds the logged
projection from an explicit allowlist of named properties, so a new field added to
`Person` is simply absent from the log until someone deliberately adds a line for
it, and renaming a masked property is a compile error instead of a silently-stale
string — a by-name mask list (the more common approach) instead fails open: it
stays silent about any field nobody thought to add, including a computed property
like `FullName` that carries the same PII under a different name.
`PersonDestructuringPolicyTests` locks the guarantee in.

## Known limitations / possible next steps

- `Console.ReadLine()` doesn't observe the cancellation token — Ctrl+C interrupts
  in-flight network calls, but not a blocking prompt for input.
- The decorator-wiring in `Program.cs` is manual and wouldn't scale gracefully past
  one decorator; a hand-rolled decorate-helper, the `Scrutor` package, or a
  middleware/pipeline style would be the options if a second one is added.
- `PersonGender` is a closed enum (`Male`/`Female`/`Unknown`); a value outside that
  set from the server would fail deserialization for the whole page it's on.

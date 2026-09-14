# 4. Patient search stays a GET, and the query string is redacted where it is copied

Date: 2026-09-14

## Status

Accepted.

## Context

`GET /api/patients?search=` takes a patient name or MRN and the frontend puts the typed value
straight into it (`PatientFlow.tsx:50`, debounced at 280ms, so a four-letter surname is one request
carrying four letters of a real person's name). The API hygiene run flagged this as the standard
GET-vs-POST search tradeoff and asked for a recorded decision rather than a default.

A query string is copied into more places than the request it belongs to. The four usually named are
access logs, proxy logs, browser history and `Referer`. Two of those do not apply here and two do,
and the difference matters, because mitigating a leak that does not exist is how the real one gets
missed:

| Copy | Applies? | Why |
| --- | --- | --- |
| Browser history | **No** | The search is a `fetch`, not a navigation. Only the SPA route enters history; the fetched URL never does. |
| `Referer` | **No** | A fetch sends the *page* URL as `Referer`, not the URL being fetched. The API answers JSON, so there are no outbound links from it either. |
| Server and proxy access logs | **Yes** | IIS W3C logging writes `cs-uri-query` by default, and SmarterASP.NET is the deploy target. `Microsoft.AspNetCore` is at `Warning` in `appsettings.json`, so Kestrel's own "Request starting" line is not emitted today - but that is a log level, one `Information` away from being. |
| OpenTelemetry spans | **Yes**, and it was the one nobody named | `OpenTelemetry.Instrumentation.AspNetCore` sets `url.query` verbatim on every server span. With an OTLP endpoint configured that is the search term leaving the building, to a system chosen for its retention rather than its confidentiality. |

So the exposure is real, and it is entirely on the telemetry side: not in the browser, not in
anything the patient's own machine keeps.

## Decision

**The search stays a GET.** A search is a read. Making it `POST /api/patients/searches` would:

- misreport every search as a write in the audit trail, in the metrics, and in any proxy or WAF rule
  that classifies by method - the places PHI access is *supposed* to be visible;
- give up `AbortController` retry semantics and conditional requests the browser gets for free on a
  safe, idempotent method, which a search-as-you-type box actually uses;
- move the name from the query string into the request body, which the same access logs can be
  configured to capture and which the same spans can be configured to record. The body is quieter by
  default, not private by construction.

The tradeoff is not "GET leaks and POST does not". It is "GET leaks by default into two systems we
run, and POST costs correctness in three we also run".

**The two real copies are handled where they happen.**

- `Observability/QueryRedaction.cs` replaces the value of every sensitive query parameter -
  currently just `search` - with `REDACTED`, and `Program.cs` applies it through
  `EnrichWithHttpRequest`, which runs after the instrumentation has set `url.query`. The parameter
  *name* is kept: dropping the query wholesale would cost the one thing the attribute is read for,
  telling a search apart from a list, and a redaction that costs the reason for the field is a
  redaction that gets switched off again.
- The reverse proxy is a deployment concern, not a code one, so it is written down rather than
  coded: an IIS deployment must set `logExtFileFlags` without `UriQuery`, or scrub `cs-uri-query`
  downstream. `backend/README.md` carries this next to the other deployment settings.

**The frontend already had its half and keeps it.** `api.ts` strips the query before a path reaches
session telemetry (`const telemetryPath = path.split('?')[0]`), which is why the search term was
never in the client event stream item 3 tightened.

## Consequences

- `QueryRedaction.Sensitive` is a list one name long, and it is the thing to maintain: any
  `[FromQuery]` parameter that can carry a name, an MRN or a date of birth belongs in it. A
  `?mrn=` filter added without touching that list reintroduces exactly this, which is why the list
  sits next to a test that reads as a specification rather than inside the OpenTelemetry wiring.
- `QueryRedactionTests` asserts both halves: the function, and - through a real request against a
  running `Program` with an `ActivityListener` attached - that the span for a patient search does not
  carry the name that was searched for. The second is the one that would catch the enrichment being
  dropped during an OpenTelemetry upgrade.
- Redaction is applied to spans, not to metrics, because `url.query` is not a metric dimension;
  `http.route` is, and a route carries no values.
- This does nothing about *who* searched for whom. That is what `[Audited]` is for, and patient
  search is currently not audited at all - a viewer can search the whole register and leave no trail.
  Recorded here because it is the more serious version of the same question, and it is not this
  decision's to make.

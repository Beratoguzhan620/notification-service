# Roadmap

A notification service that other systems call to reach people by email or through an in-app inbox. A
caller sends one request; the service renders a versioned template in Turkish or English, respects the
recipient's channel preferences and delivers through RabbitMQ workers. Requests are idempotent, messages
leave the database through an outbox, failed deliveries are retried with growing delays and end up in a
dead-letter queue that an operator can replay. Every step is recorded and traced.

## Scope

- **Clients:** each calling system has its own API key (stored as a hash). Senders send and read their own
  notifications; admins manage clients and templates and handle dead letters. The first admin key comes
  from configuration.
- **Templates:** a key such as `order-shipped`, with versions per language (`tr`, `en`): subject, plain
  text and HTML. Variables like `{{name}}` are checked when a request arrives and escaped in HTML.
- **Preferences:** per client and recipient, email and in-app can be switched off. A notification for a
  switched-off channel is recorded as `Suppressed`, not sent.
- **Requests:** `POST /api/notifications` with an `Idempotency-Key`. The same key and body returns the same
  notification; the same key with a different body is refused.
- **Delivery:** email over SMTP (Mailpit locally) and an in-app inbox with read status.
- **Failures:** three attempts with growing delays (retry queues with TTL), then the dead-letter queue.
  Dead letters can be listed and replayed.
- **History:** every status change with its time: `Accepted → Queued → Sending → Delivered`, or
  `Failed` (will retry), `Suppressed`, `DeadLettered`.
- **Tracing:** OpenTelemetry from the HTTP request through the outbox and RabbitMQ to the worker, viewed in
  Jaeger.

Out of scope: SMS and push providers, scheduling a notification for later, a user interface of its own.

## Data model

| Table | Notes |
|---|---|
| `api_clients` | name, role (`Sender`, `Admin`), key prefix (unique), key hash, created, revoked |
| `templates` | key (unique), description, default language |
| `template_versions` | template, language, version, subject, text body, HTML body, created |
| `channel_preferences` | client, recipient id, channel, enabled |
| `notifications` | client, idempotency key + request hash (unique per client), recipient id and email, channel, template version, rendered subject and bodies, status, attempts, timestamps, read time, last error |
| `notification_events` | notification, status, time, detail |
| `outbox_messages` | payload, trace context, created, published, attempts, last error |
| `processed_messages` | message id, processed time (duplicate deliveries are skipped) |

## Architecture

```
src/NotificationService.Core            Entities and rules, template parsing and rendering
src/NotificationService.Infrastructure  EF Core + Npgsql, migrations, RabbitMQ topology, outbox relay, consumer, email
src/NotificationService.Api             REST API (API keys), writes notifications and outbox rows
src/NotificationService.Worker          Outbox relay and delivery consumer. Compose starts two of them.
tests/NotificationService.UnitTests
tests/NotificationService.IntegrationTests  Real PostgreSQL, RabbitMQ and Mailpit in Testcontainers
```

## Key decisions

- **RabbitMQ.Client directly, not MassTransit.** Acknowledgements, publisher confirms, retry queues and the
  dead-letter exchange are visible in the code. MassTransit comes later in the microservices-shop project.
- **Transactional outbox.** The notification, its first history entry and the outbox row are written in
  one transaction; a relay publishes with publisher confirms and marks the row. A crash between the two
  publishes again, never loses the message.
- **At-least-once, deduplicated.** The consumer records each processed message id in the same transaction
  as the final status, so a redelivered message is acknowledged without being sent again.
- **Rendered once.** The template is rendered when the request arrives and stored with the notification, so
  a retry sends exactly what was accepted, even if the template changes in between.
- **Keys are hashed.** API keys are long random strings, so a SHA-256 hash is enough to store them; the key
  is shown once when it is created.

## Stages

All five stages are done.

| # | Branch | Content |
|---|---|---|
| 01 | `stage/01-skeleton` | Projects, API clients with hashed keys, key authentication, Compose (PostgreSQL, RabbitMQ, Mailpit, Jaeger), CI |
| 02 | `stage/02-requests` | Templates with versions and languages, preferences, idempotent notification request, outbox |
| 03 | `stage/03-delivery` | Worker: outbox relay with confirms, consumer, email and inbox, retry queues, dead letters, duplicate check |
| 04 | `stage/04-operations` | History and inbox API, dead-letter list and replay, OpenTelemetry with Jaeger, end-to-end flow |
| 05 | `stage/05-docs` | README (EN + TR), architecture, design decisions, screenshots and walkthrough |

## Later

- Send the background-jobs reports through this service.
- SMS and push channels behind the same sender interface.
- Per-client rate limits and quiet hours.

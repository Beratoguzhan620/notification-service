# Notification Service

**English** · [Türkçe](README.tr.md)

A service other systems call to reach people by email or through an in-app inbox. It renders versioned Turkish and English templates, respects each recipient's channel preferences and delivers through RabbitMQ workers. Requests are idempotent, messages leave the database through an outbox, failed deliveries are retried and end up in a dead-letter queue that can be replayed, and every notification is traced from the request to the mailbox.

[![CI](https://github.com/Beratoguzhan620/notification-service/actions/workflows/ci.yml/badge.svg)](https://github.com/Beratoguzhan620/notification-service/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![RabbitMQ 4.2](https://img.shields.io/badge/RabbitMQ-4.2-FF6600)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791)
![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-Jaeger-1F2A37)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

![Walkthrough: the end-to-end flow in a terminal, the email it sends, the trace of a notification with its retries and the queues](docs/screenshots/demo.gif)

## Try it

```bash
docker compose up --build
```

This starts PostgreSQL, RabbitMQ, Mailpit, Jaeger, the API and **two workers**. Then:

- API reference: http://localhost:8080/scalar. Use the admin key `ns_localadm_local-admin-key-change-me-in-the-env-file` (a local default; change it in `.env`).
- Mailbox (Mailpit): http://localhost:8025, where every email lands.
- Traces (Jaeger): http://localhost:16686
- Queues (RabbitMQ): http://localhost:15672, `notifications` / `rabbit-local`.
- The whole flow in one go: `tests/e2e/flow.sh` (needs `curl` and `jq`).

Sending a notification:

```bash
curl -X POST http://localhost:8080/api/notifications \
  -H "X-Api-Key: ns_localadm_local-admin-key-change-me-in-the-env-file" \
  -H "Idempotency-Key: order-1001-shipped" \
  -H "Content-Type: application/json" \
  -d '{
        "recipient": { "id": "customer-42", "email": "ayse@example.com" },
        "channel": "Email",
        "template": "order-shipped",
        "language": "tr",
        "variables": { "name": "Ayşe", "orderNo": "SIP-1001", "carrier": "Yurtiçi Kargo", "trackingNo": "YK123456789" }
      }'
```

## Features

- **Clients and keys:** every calling system has its own API key; only a hash is stored and the key is shown once. Senders send and read their own notifications; admins also manage clients and templates and handle dead letters. The first admin key comes from configuration.
- **Templates:** a key such as `order-shipped` with versions per language (`tr`, `en`): subject, plain text and HTML. Placeholders like `{{name}}` are checked when a version is saved; a request with missing values is refused and names them. Values are HTML-escaped in the HTML body and cannot add lines to the subject. A preview endpoint renders with sample values. Compose starts with two sample templates.
- **Idempotent requests:** `POST /api/notifications` needs an `Idempotency-Key`. The same key and body returns the same notification (`Idempotent-Replayed: true`), even for ten identical requests at once; the same key with a different body is refused.
- **Preferences:** per client and recipient, email or in-app can be switched off. The notification is then recorded as `Suppressed` instead of being sent.
- **Delivery:** email over SMTP (Mailpit locally) with a stable `Message-ID`, and an in-app inbox with read status, an unread count and "mark all as read".
- **Retries and dead letters:** three attempts with growing delays (5 s, then 30 s by default) through retry queues, then the dead-letter queue. Admins list dead letters with their last error and replay them.
- **History:** every status change with its time and detail: `Accepted → Queued → Sending → Delivered`, or `Failed` (another attempt follows), `Suppressed`, `DeadLettered`.
- **Tracing:** one OpenTelemetry trace per notification, from the HTTP request through the outbox relay and RabbitMQ to the worker and the SMTP call, in Jaeger.
- **Operations:** health checks for the API and each worker, structured logs with correlation ids and masked email addresses, cursor-paged lists.

## Screenshots

| The email (Turkish) | The email (English) |
|---|---|
| ![Order shipped email in Turkish](docs/screenshots/email.png) | ![Order shipped email in English](docs/screenshots/email-en.png) |
| **A trace with retries** | **A replayed dead letter, delivered** |
| ![Jaeger trace with three attempts](docs/screenshots/trace-retries.png) | ![Jaeger trace of a replay and its delivery](docs/screenshots/trace.png) |
| **Queues** | **Mailbox** |
| ![RabbitMQ queues](docs/screenshots/queues.png) | ![Mailpit inbox](docs/screenshots/mailpit.png) |
| **API reference** | |
| ![API reference](docs/screenshots/api-reference.png) | |

## Tech stack

- ASP.NET Core 10 minimal APIs, ProblemDetails, OpenAPI with Scalar
- RabbitMQ 4.2 with the official .NET client (RabbitMQ.Client 7): quorum queues, publisher confirms, manual acknowledgements, TTL retry queues and dead-lettering
- Entity Framework Core 10 with PostgreSQL (Npgsql), migrations, `FOR UPDATE SKIP LOCKED`, `xmin` row versions
- MailKit for SMTP, Mailpit as the local mail server
- OpenTelemetry tracing exported over OTLP to Jaeger
- Serilog with correlation ids and masked personal data
- xUnit, Testcontainers (real PostgreSQL, RabbitMQ and Mailpit), an end-to-end shell flow against Compose
- GitHub Actions: warnings as errors, tests, pending-migration check, Compose smoke test with two workers, end-to-end flow
- Docker / Docker Compose

## Architecture

```mermaid
flowchart LR
    caller([Calling system]) -- "X-Api-Key<br/>Idempotency-Key" --> api

    subgraph apiproc [NotificationService.Api]
        api[REST API<br/>clients, templates, preferences,<br/>notifications, inbox, dead letters]
    end

    api -- "one transaction:<br/>notification + history + outbox" --> db[(PostgreSQL)]

    subgraph workers [NotificationService.Worker × 2]
        relay[Outbox relay]
        consumer[Delivery consumer]
    end

    db -- "SKIP LOCKED" --> relay
    relay -- "publish, confirm" --> deliver[[notifications.deliver]]
    deliver --> consumer
    consumer -- "failed: copy + ack" --> retry[[notifications.retry.5000ms<br/>notifications.retry.30000ms]]
    retry -- "TTL expires" --> deliver
    consumer -- "out of attempts: reject" --> dead[[notifications.dead]]
    consumer -- "status, processed id" --> db
    consumer -- "SMTP" --> mail[(Mailpit)]
    api & relay & consumer -. "OTLP" .-> jaeger[(Jaeger)]
```

How a notification moves through its statuses:

```mermaid
stateDiagram-v2
    [*] --> Accepted: request saved with its outbox row
    Accepted --> Queued: relay published it
    Queued --> Sending: a worker claims it
    Queued --> Suppressed: channel switched off
    Sending --> Delivered
    Sending --> Failed: attempt failed
    Failed --> Sending: after the retry delay
    Sending --> DeadLettered: last attempt failed
    DeadLettered --> Accepted: admin replays it
```

## Design decisions

- **RabbitMQ.Client directly, not MassTransit.** Acknowledgements, publisher confirms, retry queues and the dead-letter exchange are written out in the code, so every guarantee can be pointed at. MassTransit comes later, in the microservices project, where its sagas earn their place.
- **Transactional outbox.** The notification, its first history entry and the outbox row are written in one transaction, so there is never a message for a request that was rolled back, nor a saved request without a message. A relay in each worker takes unpublished rows with `FOR UPDATE SKIP LOCKED` (two workers share the work without waiting on each other), publishes each with a confirm and only then marks it. A crash in between publishes again; it never loses a message.
- **At-least-once delivery, deduplicated twice.** The consumer records each finished message id in the same transaction as the final status, and a message id found there is acknowledged without work. The notification's own status is the second guard. Both are needed: after a replay the notification is active again, and only the message id tells an old duplicate from the new message.
- **No transaction stays open during SMTP.** A delivery is three steps: claim (lock the row, check duplicates and preferences, move to `Sending`), send, record. A worker that dies mid-send leaves a `Sending` row that another worker may take over once a lease runs out; until then a second copy of the message waits in a retry queue instead of sending twice.
- **Retries without a scheduler.** A failed message is copied to a retry queue whose messages expire after the delay and are dead-lettered back to the delivery queue; the original is acknowledged only after RabbitMQ confirmed the copy. Retry queues use at-least-once dead-lettering, so expiring messages cannot be lost on the way back. The delay is part of the queue name, so changing it never clashes with an existing queue.
- **Rendered once.** The template is rendered when the request arrives and stored with the notification. Every attempt sends exactly what was accepted, and the history names the template version it came from, even after the template changes.
- **Escaping where it matters.** Values are HTML-encoded in the HTML body, kept as they are in plain text, and flattened to one line in the subject, so a value cannot add markup or a mail header.
- **Keys are hashed with SHA-256.** API keys are 32 random bytes, so there is nothing to guess the way there is with a password; a slow hash would only slow every request. The key's short prefix finds the client and the comparison runs in constant time.
- **Preferences are checked at delivery, not at request time.** A recipient who switches email off after a request but before delivery gets nothing.

## Commands

| Command | What it does |
|---|---|
| `docker compose up --build` | PostgreSQL, RabbitMQ, Mailpit, Jaeger, the API on http://localhost:8080 and two workers |
| `docker compose up --build --scale worker=4` | The same with four workers |
| `tests/e2e/flow.sh` | The end-to-end flow against a running stack: email, idempotency, inbox, preferences, a dead letter and its replay, traces |
| `dotnet test` | Unit and integration tests (Docker needed for Testcontainers) |
| `dotnet run --project src/NotificationService.Api` | Run the API locally (configuration below) |
| `dotnet run --project src/NotificationService.Worker` | Run a worker locally |
| `dotnet ef migrations add <Name> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations` | Add a migration |

Configuration for a local run, kept out of the repository:

```bash
dotnet user-secrets --project src/NotificationService.Api set "ConnectionStrings:Notifications" "Host=localhost;Database=notifications;Username=…;Password=…"
dotnet user-secrets --project src/NotificationService.Api set "Bootstrap:AdminApiKey" "ns_<8 lowercase letters or digits>_<at least 32 characters>"
dotnet user-secrets --project src/NotificationService.Worker set "ConnectionStrings:Notifications" "Host=localhost;Database=notifications;Username=…;Password=…"
dotnet user-secrets --project src/NotificationService.Worker set "ConnectionStrings:RabbitMq" "amqp://user:password@localhost:5672/"
```

The worker reads `Smtp:Host` and `Smtp:Port` (Mailpit's `localhost:1025` by default); both processes export traces when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Tests

122 tests: 58 unit tests and 64 integration tests that run the real API, outbox relay and consumer against PostgreSQL, RabbitMQ and Mailpit in Testcontainers, plus an end-to-end flow against the Compose stack in CI.

- **Delivery:** an email goes from the request to the Mailpit mailbox with escaped HTML and a stable `Message-ID`; in-app notifications need no address; a switched-off channel is suppressed and nothing is sent.
- **Failures:** an address that keeps failing is tried three times and dead-lettered (and its message is in the dead queue); a temporary failure is retried until it works; a dead letter is replayed and delivered.
- **Exactly one email:** the same message delivered again, an outbox row published twice, and ten identical requests at the same moment each end with one notification and one email.
- **Queues:** a message in a retry queue comes back after its delay; rejected and unreadable messages end up in the dead queue.
- **Requests:** idempotency per client, variable order and formatting not changing the request, missing variables, unknown templates, the language fallback, header injection in the subject.
- **Access:** malformed, unknown, altered and revoked keys; senders kept out of admin endpoints and away from other clients' notifications and inboxes.
- **Tracing:** the request, publish, process and delivery spans share one trace with the right parents.
- **End-to-end (CI):** the flow above against Compose, including Mailpit's chaos mode refusing every recipient to produce a real dead letter, and the traces arriving in Jaeger.

## Roadmap and known gaps

- Send the [background-jobs](https://github.com/Beratoguzhan620/background-jobs) reports through this service.
- SMS and push channels behind the same channel interface.
- Per-client rate limits and quiet hours.
- Clean up old `processed_messages` and published outbox rows with a scheduled job.
- API keys are looked up in the database on every request; a short cache would cut that, at the cost of revocation taking effect a little later.

## License

[MIT](LICENSE) · Berat Oğuzhan Turğut · [GitHub](https://github.com/Beratoguzhan620)

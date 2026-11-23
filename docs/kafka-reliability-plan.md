# Kafka reliability plan

Step 1 of making PropTrack production-grade. The goal is that no event is lost or
applied twice, and that this is proven by tests rather than asserted.

Status: drafted 2026-11-13. Slices 0 to 2 built on branch `kafka-reliability` (tests in
`src/DealsService/DealsService.Tests/Reliability`). The dual-write test stays red until
slice 3.

## The problem today

- **Dual write.** Services save to Postgres, then publish to Kafka as a separate step.
  A crash or broker failure between the two leaves the database changed and no event
  sent. Example: `src/DealsService/DealsService.Business/DealService.cs:97`.
- **Auto-commit.** `src/Shared/PropTrack.Messaging/KafkaConsumerService.cs:66` commits
  offsets on a timer, independent of whether the handler finished, so a crash
  mid-handling can skip a message.
- **Log and drop.** A handler exception is logged and the message is discarded
  (`KafkaConsumerService.cs:102`). There is no retry and no dead-letter topic.
- **No event identity.** Event payloads carry no unique ID, so a consumer cannot tell a
  redelivery from a new event.
- The Python consumer (`src/IngestionService/app/kafka_io.py`) mirrors the same
  auto-commit and log-and-drop behaviour.

## Decisions

- **Hand-built, not MassTransit or another library.** This is a learning project; a
  library would hide the parts that need to be understood and explained.
- **Polling outbox relay, not change-data-capture (Debezium).** Simpler and sufficient
  here. CDC trades more infrastructure for lower latency.
- **Vertical slice first.** Build on the deals-service → listings-service path
  (`DealCreated`), then roll out to the other services.
- **Snapshot events via the outbox carry only the entity ID.** The relay re-reads and
  publishes the current snapshot. The snapshot topics are compacted, so only the latest
  state matters.
- **ai-service's rationale worker gets slices 1 and 2 only.** Its publish is not a
  database-plus-Kafka dual write, so it gets no outbox.

## Slices

All slices live on one branch, `kafka-reliability`: one commit (or a few) per slice, and a
single pull request once every slice is complete. Run them in order, not in parallel:
slices 1 to 4 all change the shared messaging library.

### 0. Prove the loss

Add Kafka and Postgres test containers to `DealsService.Tests`, following the pattern in
`src/AiService/AiService.Tests/RedisFixture.cs` (skip with a reason when Docker is
unavailable). Write two tests:

1. The publisher fails after the database commit: the deal exists and no event was sent.
2. A handler throws once: the message is never processed.

Done when both tests fail for the right reason.

### 1. Manual offset commits

In `KafkaConsumerService`, set `EnableAutoCommit = false` and commit the offset only
after `HandleAsync` succeeds.

Teaches: at-least-once delivery, offset commits.
Done when a crash mid-handling causes the message to be redelivered.

### 2. Retry and dead-letter topic

Retry a failed message a small number of times with backoff. If it still fails, publish
it to `<topic>.dlt` with headers for the error, original topic, partition and offset,
then commit. Messages that cannot be deserialized go straight to the dead-letter topic.
`TopicProvisioner` creates the dead-letter topics.

As built: retries run in memory with the partition waiting (`Kafka:ConsumerMaxAttempts`,
default 3; `Kafka:ConsumerRetryBackoffMs`, default 1000, doubling). Each consumer
provisions its own dead-letter topic at startup, with unlimited retention. A dead letter
keeps the original key, value and headers and adds `dlt.*` headers (see `DeadLetter.cs`).
If the dead-letter publish itself fails, the message stays uncommitted and is retried.

Teaches: poison messages.
Done when a poison message lands in the dead-letter topic and the next message is still
processed.

### 3. Outbox in deals-service

- New `outbox_messages` table (ID, topic, key, payload, created-at, sent-at).
- Events are added in the same transaction as the domain change. Today the services
  publish after the repository has already called `SaveChangesAsync`, so event creation
  has to move inside the save. This is the largest change in the plan.
- A background relay reads unsent rows with `FOR UPDATE SKIP LOCKED`, publishes them and
  marks them sent. Safe to run on more than one replica.
- Turn on producer idempotence in `KafkaEventPublisher`.

Teaches: the outbox pattern.
Done when the slice 0 publisher test passes.

### 4. Idempotent consumers

- Send the outbox row ID as an event ID in a Kafka header.
- Audit all eight handlers. Those that are naturally safe to run twice (for example
  version-checked search indexing) stay as they are. The rest record the event ID in a
  `processed_messages` table in the same transaction as their effect.

Teaches: deduplication under at-least-once delivery.
Done when delivering the same message twice has exactly one effect.

### 5. Roll out

- Outbox in listings-service.
- Manual commit, retry and dead-letter behaviour in the Python consumer.
- A small script to replay a dead-letter topic back onto its source topic.
- A write-up of before and after, with the test results.

Done when every service behaves the same way.

## Consumers to cover

| Service | Consumer |
|---|---|
| listings-service | `DealCreatedConsumer`, `DealOutcomeRecordedConsumer`, `AiPropertySummaryReadyConsumer` |
| deals-service | `AiDealRationaleReadyConsumer` |
| documents-service | `DealDocumentUploadedConsumer` |
| search-service | `PropertySnapshotConsumer`, `DealSnapshotConsumer` |
| ai-service | `DealRationaleConsumer` |
| ingestion-service | `consume_loop` in `kafka_io.py` |

## What comes after

Step 2 is CI and integration tests (built around the tests from this step), step 3 is
observability, step 4 is deploying, load-testing and failure-testing the system.

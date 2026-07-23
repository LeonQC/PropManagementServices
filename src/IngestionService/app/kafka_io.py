import json
import logging
import threading

from confluent_kafka import Consumer, Producer

from .config import settings
from .pipeline import ingest

log = logging.getLogger("kafka")

_producer: Producer | None = None


def _get_producer() -> Producer:
    global _producer
    if _producer is None:
        _producer = Producer({"bootstrap.servers": settings.kafka_bootstrap})
    return _producer


def publish_processed(payload: dict, key: str) -> None:
    p = _get_producer()
    p.produce(settings.topic_out, key=key, value=json.dumps(payload).encode("utf-8"))
    p.flush(10)


def consume_loop(stop: threading.Event) -> None:
    """Single-topic consume loop (mirrors the shared .NET KafkaConsumerService:
    JSON payloads, per-message error isolation, auto-commit)."""
    consumer = Consumer({
        "bootstrap.servers": settings.kafka_bootstrap,
        "group.id": settings.kafka_group_id,
        "auto.offset.reset": "earliest",
        "enable.auto.commit": True,
    })
    consumer.subscribe([settings.topic_in])
    log.info("Subscribed to %s as group %s", settings.topic_in, settings.kafka_group_id)

    try:
        while not stop.is_set():
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                # 'Unknown topic' noise before the first publish is expected.
                log.warning("Consume error on %s: %s", settings.topic_in, msg.error())
                continue
            try:
                event = json.loads(msg.value())
                ingest(event, publish_processed)
            except Exception as ex:  # noqa: BLE001 — one poison message must not stall the topic
                log.error("Failed handling message at offset %s: %s", msg.offset(), ex, exc_info=True)
    finally:
        consumer.close()
        log.info("Consumer closed.")

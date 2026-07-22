import logging
import tempfile
from pathlib import Path

import boto3
from botocore.config import Config

from .config import settings

log = logging.getLogger("storage")

PARSED_PREFIX = "parsed/"

_client = boto3.client(
    "s3",
    endpoint_url=settings.s3_endpoint,
    aws_access_key_id=settings.s3_access_key,
    aws_secret_access_key=settings.s3_secret_key,
    config=Config(s3={"addressing_style": "path"}),  # MinIO: buckets as path segments
)


def resolve_key(document_id: str) -> str | None:
    """The Kafka event carries only the /documents/v1/{id} pointer; blobs are
    keyed '{documentId}/{fileName}', so a prefix listing resolves the object
    (single object per prefix — parsed artifacts live under parsed/, not here)."""
    resp = _client.list_objects_v2(Bucket=settings.s3_bucket, Prefix=f"{document_id}/", MaxKeys=2)
    contents = resp.get("Contents", [])
    if not contents:
        return None
    if len(contents) > 1:
        log.warning("Multiple blobs under prefix %s/ — using the first.", document_id)
    return contents[0]["Key"]


def download_to_temp(key: str) -> Path:
    """Download keeping the original suffix — Docling sniffs format from it."""
    suffix = Path(key).suffix or ".bin"
    tmp = tempfile.NamedTemporaryFile(suffix=suffix, delete=False)
    tmp.close()
    _client.download_file(settings.s3_bucket, key, tmp.name)
    return Path(tmp.name)


def put_parsed(document_id: str, markdown: str) -> str:
    key = f"{PARSED_PREFIX}{document_id}.md"
    _client.put_object(
        Bucket=settings.s3_bucket, Key=key,
        Body=markdown.encode("utf-8"), ContentType="text/markdown; charset=utf-8",
    )
    return key


def get_parsed(document_id: str) -> str | None:
    try:
        resp = _client.get_object(Bucket=settings.s3_bucket, Key=f"{PARSED_PREFIX}{document_id}.md")
        return resp["Body"].read().decode("utf-8")
    except _client.exceptions.NoSuchKey:
        return None

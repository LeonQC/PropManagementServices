from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    """All configuration is env-driven (compose supplies the in-network values)."""

    database_url: str = "postgresql://proptrack:proptrack@localhost:5437/proptrack_rag"

    kafka_bootstrap: str = "localhost:29092"
    kafka_group_id: str = "ingestion-service"
    topic_in: str = "deal.document_uploaded"
    topic_out: str = "document.processed"

    s3_endpoint: str = "http://localhost:9000"
    s3_access_key: str = "minioadmin"
    s3_secret_key: str = "minioadmin"
    s3_bucket: str = "proptrack-documents"

    litellm_base_url: str = "http://localhost:4000"
    litellm_api_key: str = "dummy"  # proxy runs without a master key in dev
    embedding_model: str = "embed-openai"
    embedding_dimensions: int = 1024

    jwks_url: str = "http://localhost:5300/auth/v1/.well-known/jwks.json"
    jwt_issuer: str = "proptrack-auth"
    jwt_audience: str = "proptrack"

    # The deals record's storageUrl pointer prefix that marks a real upload
    # (same contract as documents-service and the UI).
    storage_url_prefix: str = "/documents/v1/"

    @property
    def embedding_model_tag(self) -> str:
        """Stored on every chunk row; query-time embeddings must match it."""
        return f"{self.embedding_model}@{self.embedding_dimensions}"


settings = Settings()

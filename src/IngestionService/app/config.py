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

    # --- lexical / hybrid retrieval (OpenSearch BM25) ---
    opensearch_url: str = "http://localhost:9200"
    opensearch_index: str = "document_chunks_v1"
    opensearch_timeout: float = 5.0

    # Dual-write toggle. Off means the service behaves exactly as it did before hybrid
    # existed; the index simply stops receiving new chunks.
    lexical_enabled: bool = True

    # Default mode when a request omits one: dense | lexical | hybrid.
    search_mode: str = "dense"

    # RRF's rank-smoothing constant. The conventional 60 was tuned for TREC runs of ~1000;
    # over a deal's ~17 chunks it flattens the fused score badly (rank-1 to rank-20 weight
    # ratio 1.31x, against 2.73x at k=10). Swept rather than assumed — see docs/retrieval-eval.md.
    rrf_k: int = 60

    # Per-source candidate depth. MUST stay independent of the request's topK: the eval
    # harness caches one ranked list per question and truncates it to simulate smaller
    # topK values, which is only valid if a shorter request returns a prefix of a longer
    # one. Deriving this from topK would make that optimisation silently wrong.
    candidate_k: int = 50

    lexical_min_should_match: str | None = None

    # --- cross-encoder reranking (HuggingFace TEI) ---
    rerank_url: str = "http://localhost:8081"

    # 10s against a measured worst case of 1.2s for 32 pairs of the longest chunks in the
    # corpus. Generous rather than tight on purpose: the fallback is "no rerank", not a
    # failed request, so a slow reranker should be allowed to finish rather than degrade a
    # search that would otherwise have succeeded.
    rerank_timeout: float = 10.0

    # Defaults OFF so that a bare checkout, or any deployment without a tei-rerank
    # container, behaves exactly as it did before reranking existed rather than degrading
    # every search. Compose turns it on — that is where the deployment decision lives, the
    # same split as search_mode above.
    rerank_enabled: bool = False

    # Rerank depth. MUST stay independent of the request's topK, for exactly the reason
    # candidate_k above must: the eval harness caches one ranked list per question and
    # truncates it to simulate smaller topK values, which is only valid if a shorter
    # request returns a prefix of a longer one. `max(rerank_candidates, topK)` would break
    # that silently. 30 covers a deal's whole haystack (~17 chunks, max 33) while capping
    # unscoped latency below candidate_k.
    rerank_candidates: int = 30

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

# PropTrack

An AI-powered deal management platform for commercial real estate acquisitions. Teams track properties and deals through a pipeline, attach due-diligence documents, and ask questions that are answered from those documents with citations.

The React client lives in a separate repo: [PropManagementServices-UI](https://github.com/zephaniezhu/PropManagementServices-UI).

## Highlights

- **Seven microservices** (ASP.NET Core and Python/FastAPI), each owning its own PostgreSQL database and communicating over REST and Apache Kafka.
- **RAG over deal documents.** PDFs are parsed with Docling, chunked, embedded into pgvector, and reranked with a cross-encoder. Claude answers with citations, streamed over Server-Sent Events.
- **LLM agent.** A bounded tool-use loop over six read-only tools (deal, property, and document search; pipeline summary). The user's JWT is forwarded on every tool call, so the model can only reach data the user is authorized to see.
- **Measured, not eyeballed.** A 100-question labeled eval set built from 521 PDFs drives retrieval tuning: recall@final 0.80 → 1.00, 0.93 faithfulness (RAGAS, cross-provider judge). Full write-up: [docs/retrieval-eval.md](docs/retrieval-eval.md).
- **Full-text search** on OpenSearch (custom analyzers, edge n-gram autocomplete, fuzzy matching), rebuilt by replaying compacted Kafka topics.
- **Cost and abuse controls** for LLM endpoints: per-request cost/latency tracking, per-user Redis rate limiting that holds across replicas, and a Kafka worker that only calls the model when a deal's score changes materially.

## Architecture

```
                         React client (Vite, TypeScript)
                                      │  REST / SSE
   ┌──────────┬──────────┬───────────┼───────────┬────────────┐
   ▼          ▼          ▼           ▼           ▼            ▼
 auth     listings     deals     documents    search         ai ──► Claude API
(JWT/     (Postgres)  (Postgres) (Postgres +  (OpenSearch)   (Postgres, Redis)
 JWKS)                            MinIO/S3)                   │
                                      │                       ▼
                                      └── Kafka ──►  ingestion (FastAPI)
                                                     Docling → pgvector
                                                     LiteLLM → embeddings, reranker
```

| Service | Stack | Responsibility |
|---|---|---|
| auth-service | ASP.NET Core, Identity | Login, users, RS256 JWTs published via JWKS |
| listings-service | ASP.NET Core, EF Core | Properties; publishes `property.*` events |
| deals-service | ASP.NET Core, EF Core | Deal pipeline, tasks, comments, deal scoring; publishes `deal.*` events |
| documents-service | ASP.NET Core, S3 SDK | Presigned uploads to MinIO/S3, document records |
| ingestion-service | Python, FastAPI | Parse, chunk, embed, and search deal documents (pgvector + reranking) |
| search-service | ASP.NET Core, OpenSearch | Full-text search, fed by compacted snapshot topics |
| ai-service | ASP.NET Core, Anthropic SDK | Deal Q&A, the assistant agent, background deal rationales |

Every service validates tokens against auth-service's JWKS endpoint. ai-service holds no service account: it forwards the caller's own token downstream.

## Running locally

Requirements: Docker with **~12 GB of memory** allocated (the embedding and reranker models are the large containers), plus OpenAI and Anthropic API keys.

```bash
cp .env.example .env        # add OPENAI_API_KEY and ANTHROPIC_API_KEY
docker compose up -d
```

Then start the [UI](https://github.com/zephaniezhu/PropManagementServices-UI) (`npm install && npm run dev`, served on http://localhost:5173) and sign in with the bootstrap admin from `src/AuthService/AuthService.Api/appsettings.json`.

| Service | Port | | Service | Port |
|---|---|---|---|---|
| listings | 5100 | | search | 5600 |
| deals | 5200 | | ai | 5700 |
| auth | 5300 | | OpenSearch Dashboards | 5601 |
| documents | 5400 | | MinIO console | 9001 |
| ingestion | 5500 | | LiteLLM proxy | 4000 |

Documents are loaded through the real upload flow with `scripts/seed_deal_documents.py` (see the script header for usage). The PDF corpus itself is not included in this repo.

## Evaluation

```bash
python3 scripts/eval_retrieval.py --sweep                           # retrieval only, no LLM, stdlib-only
scripts/.venv/bin/python scripts/eval_ragas.py --llm-judge --repeats 3   # generation, judged with RAGAS
```

Method, results, and limitations are in [docs/retrieval-eval.md](docs/retrieval-eval.md), including a BM25 hybrid-search experiment that was built, measured, and removed once reranking made it redundant.

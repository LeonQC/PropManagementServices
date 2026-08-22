from dataclasses import dataclass

from langchain_text_splitters import RecursiveCharacterTextSplitter

from .parsing import ParsedDocument

# RAG-Challenge-2's winning configuration: ~300-token chunks with 50-token
# overlap, split page by page so every chunk keeps its page for citations.
#
# KNOWN, ACCEPTED MISMATCH — read this before raising chunk_size.
#
# "300 tokens" here means 300 *tiktoken gpt-4o* tokens, which is not the tokenizer of any
# model that consumes these chunks. It does not matter for the embedding models (both have
# ~8k windows and never bind), but the cross-encoder reranker does have a hard limit: 512
# tokens, counted in XLM-RoBERTa/SentencePiece (BAAI/bge-reranker-base — see app/rerank.py).
# Reranking goes through the LiteLLM proxy, whose transform hard-codes `truncate: false`,
# so one chunk over 512 returns HTTP 422 and drops its ENTIRE batch. That surfaces as
# `degraded: true` and a silently worse ordering, not an error anyone notices.
#
# Measured across all 1,147 chunks in the seeded corpus, bge tokens ÷ tiktoken tokens:
# median 1.08, p95 1.27, max 1.43 — and that 1.43 is a 14-token form-field fragment, all
# underscores. Among chunks actually at the 300 target, the worst is 350 bge tokens
# (~1.17x). Crossing 512 from a 300-token chunk needs 1.71x. Nothing in this corpus is
# close, which is why sizing here in the reranker's own tokenizer was tried and then
# reverted: it added a `transformers` import and a tokenizer bake into the image to buy
# margin that was already comfortable.
#
# What would eat that margin, since the mechanism is visible in the worst case above:
# SentencePiece fragments underscore runs, long numeric strings, and non-Latin text far
# harder than cl100k does. A chunk composed mostly of those could plausibly reach ~1.4.
# So if this service starts ingesting document types unlike the templated CRE PDFs it was
# tuned on — or if chunk_size goes up, or the reranker is swapped for one with a smaller
# window — re-measure before trusting this. Above 300, a 512-token chunk stops being
# hypothetical: at chunk_size=400 it only takes 1.28x, which is inside the p95 already.
_splitter = RecursiveCharacterTextSplitter.from_tiktoken_encoder(
    model_name="gpt-4o", chunk_size=300, chunk_overlap=50,
)


@dataclass
class Chunk:
    index: int
    page_no: int | None
    text: str


def chunk(parsed: ParsedDocument) -> list[Chunk]:
    chunks: list[Chunk] = []
    for page in parsed.pages:
        for piece in _splitter.split_text(page.text):
            if piece.strip():
                chunks.append(Chunk(index=len(chunks), page_no=page.page_no, text=piece))
    return chunks

from dataclasses import dataclass

from langchain_text_splitters import RecursiveCharacterTextSplitter

from .parsing import ParsedDocument

# RAG-Challenge-2's winning configuration: ~300-token chunks with 50-token
# overlap, split page by page so every chunk keeps its page for citations.
#
# KNOWN, ACCEPTED MISMATCH — re-measure before raising chunk_size.
#
# "300 tokens" is tiktoken gpt-4o, not the tokenizer of any model consuming these chunks.
# Harmless for the embedding models (~8k windows), but the reranker has a hard 512-token
# limit counted in XLM-RoBERTa/SentencePiece, and rerank.py goes through LiteLLM, which
# cannot truncate — one oversized chunk 422s its whole batch and degrades silently.
#
# Measured over 1,147 corpus chunks, bge/tiktoken ratio: median 1.08, p95 1.27, max 1.43.
# Crossing 512 from 300 needs 1.71x, so the margin is wide — but it narrows fast: at
# chunk_size=400 it takes only 1.28x. Sizing here in the reranker's own tokenizer was
# implemented and reverted as not worth the dependency; see docs/retrieval-eval.md.
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

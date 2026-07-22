from dataclasses import dataclass

from langchain_text_splitters import RecursiveCharacterTextSplitter

from .parsing import ParsedDocument

# RAG-Challenge-2's winning configuration: ~300-token chunks with 50-token
# overlap, split page by page so every chunk keeps its page for citations.
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

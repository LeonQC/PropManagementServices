import logging
from dataclasses import dataclass
from pathlib import Path

from docling.datamodel.base_models import InputFormat
from docling.datamodel.pipeline_options import PdfPipelineOptions, TableFormerMode
from docling.document_converter import DocumentConverter, PdfFormatOption
from docling_core.types.doc import TableItem

log = logging.getLogger("parsing")


@dataclass
class PageText:
    page_no: int | None  # None for formats without page semantics (docx/xlsx)
    text: str


@dataclass
class ParsedDocument:
    markdown: str          # full-document markdown (the stored artifact)
    pages: list[PageText]  # page-grouped text for page-aware chunking


def _converter() -> DocumentConverter:
    # RAG-Challenge-2-proven settings: accurate table structure, OCR off in v1
    # (PropTrack documents are digital exports, and OCR models bloat the image).
    pdf_opts = PdfPipelineOptions()
    pdf_opts.do_ocr = False
    pdf_opts.do_table_structure = True
    pdf_opts.table_structure_options.mode = TableFormerMode.ACCURATE
    return DocumentConverter(
        allowed_formats=[InputFormat.PDF, InputFormat.DOCX, InputFormat.XLSX, InputFormat.PPTX],
        format_options={InputFormat.PDF: PdfFormatOption(pipeline_options=pdf_opts)},
    )


_conv: DocumentConverter | None = None


def parse(path: Path) -> ParsedDocument:
    global _conv
    if _conv is None:
        _conv = _converter()  # lazy: model load is expensive, do it once

    doc = _conv.convert(path).document
    markdown = doc.export_to_markdown()

    # Group content by page so chunks carry citation metadata. Tables are
    # exported as markdown inline (structure preserved by TableFormer).
    by_page: dict[int | None, list[str]] = {}
    order: list[int | None] = []
    for item, _level in doc.iterate_items():
        page_no = item.prov[0].page_no if getattr(item, "prov", None) else None
        if isinstance(item, TableItem):
            text = item.export_to_markdown(doc)
        else:
            text = getattr(item, "text", "") or ""
        if not text.strip():
            continue
        if page_no not in by_page:
            by_page[page_no] = []
            order.append(page_no)
        by_page[page_no].append(text)

    pages = [PageText(page_no=p, text="\n\n".join(by_page[p])) for p in order]
    if not pages and markdown.strip():
        pages = [PageText(page_no=None, text=markdown)]
    return ParsedDocument(markdown=markdown, pages=pages)

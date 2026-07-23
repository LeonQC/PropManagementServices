FROM python:3.12-slim

# uv for fast, reproducible installs
COPY --from=ghcr.io/astral-sh/uv:latest /uv /uvx /bin/

# Docling's layout/table models pull in opencv-style native deps
RUN apt-get update && apt-get install -y --no-install-recommends \
        libglib2.0-0 libgl1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Build context is the repo's src/ directory (same as the .NET services).
COPY IngestionService/pyproject.toml ./
RUN uv pip install --system --no-cache .

# Bake the Docling parsing models into the image (decision: hermetic startup,
# no runtime downloads) — layout + TableFormer; OCR models excluded (OCR off in v1).
RUN docling-tools models download layout tableformer

COPY IngestionService/app ./app

EXPOSE 8080
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8080"]

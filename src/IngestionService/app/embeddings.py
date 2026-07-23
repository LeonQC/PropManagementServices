from openai import OpenAI

from .config import settings

# One OpenAI-compatible client pointed at the LiteLLM Proxy. Which backing
# model answers (OpenAI API vs local TEI/bge-m3) is LiteLLM config, not code.
_client = OpenAI(base_url=settings.litellm_base_url, api_key=settings.litellm_api_key)

_BATCH = 64


def embed(texts: list[str]) -> list[list[float]]:
    vectors: list[list[float]] = []
    for i in range(0, len(texts), _BATCH):
        batch = texts[i : i + _BATCH]
        # `dimensions` Matryoshka-truncates on the OpenAI route; LiteLLM has
        # drop_params:true so the TEI route (natively 1024) ignores it.
        resp = _client.embeddings.create(
            model=settings.embedding_model,
            input=batch,
            dimensions=settings.embedding_dimensions,
        )
        vectors.extend(d.embedding for d in resp.data)
    return vectors


def embed_one(text: str) -> list[float]:
    return embed([text])[0]

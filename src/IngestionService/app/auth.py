import logging
import threading
import time

import httpx
import jwt
from fastapi import Depends, HTTPException, Request
from jwt import PyJWK

from .config import settings

log = logging.getLogger("auth")


class JwksCache:
    """Fetches and caches the auth-service JWKS (mirrors the .NET
    JwksSigningKeyCache: short TTL because auth publishes a constant kid even if
    its key changes; an unknown kid forces a rate-limited early refresh)."""

    TTL = 300.0
    MIN_REFRESH = 30.0

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._keys: dict[str, PyJWK] = {}
        self._fetched_at = 0.0

    def key_for(self, kid: str | None) -> PyJWK | None:
        keys = self._current(force=False)
        if kid is not None and kid not in keys:
            keys = self._current(force=True)
        if kid is not None:
            return keys.get(kid)
        return next(iter(keys.values()), None)

    def _current(self, force: bool) -> dict[str, PyJWK]:
        max_age = self.MIN_REFRESH if force else self.TTL
        if time.monotonic() - self._fetched_at < max_age and self._keys:
            return self._keys
        with self._lock:
            if time.monotonic() - self._fetched_at < max_age and self._keys:
                return self._keys
            try:
                data = httpx.get(settings.jwks_url, timeout=5.0).json()
                self._keys = {k["kid"]: PyJWK(k) for k in data.get("keys", []) if "kid" in k}
                self._fetched_at = time.monotonic()
                log.info("Loaded %d signing key(s) from %s", len(self._keys), settings.jwks_url)
            except Exception as ex:  # noqa: BLE001 — keep serving last-known keys
                log.error("Failed to fetch JWKS from %s: %s", settings.jwks_url, ex)
            return self._keys


jwks = JwksCache()


def require_bearer(request: Request) -> dict:
    """FastAPI dependency: validate the RS256 bearer token, return its claims."""
    header = request.headers.get("authorization", "")
    if not header.lower().startswith("bearer "):
        raise HTTPException(status_code=401, detail="Missing bearer token")
    token = header[7:]
    try:
        kid = jwt.get_unverified_header(token).get("kid")
        key = jwks.key_for(kid)
        if key is None:
            raise HTTPException(status_code=401, detail="No signing key available")
        return jwt.decode(
            token,
            key,
            algorithms=["RS256"],
            issuer=settings.jwt_issuer,
            audience=settings.jwt_audience,
            leeway=30,
        )
    except HTTPException:
        raise
    except jwt.PyJWTError as ex:
        raise HTTPException(status_code=401, detail=f"Invalid token: {ex}") from ex


Claims = Depends(require_bearer)

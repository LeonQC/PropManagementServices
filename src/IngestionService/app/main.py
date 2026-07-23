import logging
import threading
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse

from .api import _meta, router
from .db import init_db
from .kafka_io import consume_loop

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s [%(name)s] %(message)s")
log = logging.getLogger("main")

_stop = threading.Event()


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    thread = threading.Thread(target=consume_loop, args=(_stop,), name="kafka-consumer", daemon=True)
    thread.start()
    log.info("ingestion-service started.")
    yield
    _stop.set()
    thread.join(timeout=10)


app = FastAPI(title="PropTrack ingestion-service", lifespan=lifespan, openapi_url=None)
app.include_router(router)


@app.exception_handler(HTTPException)
async def error_envelope(request: Request, exc: HTTPException):
    """Errors use the house {error: {...}} envelope (architecture §5.1)."""
    code = "UNAUTHORIZED" if exc.status_code == 401 else "NOT_FOUND" if exc.status_code == 404 else "ERROR"
    meta = _meta()
    return JSONResponse(
        status_code=exc.status_code,
        content={"error": {"code": code, "message": str(exc.detail), "details": [],
                           "requestId": meta["requestId"], "timestamp": meta["timestamp"]}},
    )

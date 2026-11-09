"""Serve the locally installed, qualified Chatterbox Multilingual V3 voices."""

from __future__ import annotations

import hashlib
import io
import json
import os
from contextlib import asynccontextmanager
from pathlib import Path
from threading import Lock

ROOT = Path(__file__).resolve().parents[1]
MODELS = Path(os.environ.get("LOCALDUB_MODELS_ROOT", ROOT / ".localdub/models"))
MODEL_DIRECTORY = MODELS / "chatterbox-multilingual"
os.environ.update(
    HF_HUB_OFFLINE="1",
    TRANSFORMERS_OFFLINE="1",
    HF_HUB_DISABLE_TELEMETRY="1",
    DO_NOT_TRACK="1",
    PKUSEG_HOME=str(MODELS / "pkuseg"),
    NUMBA_CACHE_DIR=str(ROOT / ".localdub/cache/multilingual/numba"),
)

import numpy as np
import soundfile as sf
import torch
from chatterbox.models.tokenizers import tokenizer
from chatterbox.mtl_tts import ChatterboxMultilingualTTS
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response
from pydantic import BaseModel, Field

model: ChatterboxMultilingualTTS | None = None
synthesis_lock = Lock()


def local_tokenizer_asset(repo_id, filename, **kwargs):
    if (repo_id, filename) != ("ResembleAI/chatterbox", "Cangjie5_TC.json"):
        raise RuntimeError(f"Unexpected tokenizer asset: {repo_id}/{filename}")
    return str(MODEL_DIRECTORY / filename)


@asynccontextmanager
async def lifespan(_: FastAPI):
    global model
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA is required for the installed OD voices")
    manifest = json.loads((MODEL_DIRECTORY / "models.lock.json").read_text(encoding="utf-8"))
    assets = [(MODEL_DIRECTORY / item["name"], item) for item in manifest["files"]]
    assets += [(MODELS / "pkuseg" / item["name"], item) for item in manifest["auxiliary"]["files"]]
    for path, item in assets:
        with path.open("rb") as source:
            if hashlib.file_digest(source, "sha256").hexdigest() != item["sha256"]:
                raise RuntimeError(f"Model checksum mismatch: {path.name}")
    tokenizer.hf_hub_download = local_tokenizer_asset
    model = ChatterboxMultilingualTTS.from_local(MODEL_DIRECTORY, device="cuda", t3_model="v3")
    try:
        yield
    finally:
        model = None
        torch.cuda.empty_cache()


app = FastAPI(title="LocalDub Multilingual V3", lifespan=lifespan)


class TtsRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    language: str = "en"
    reference: str
    temperature: float = Field(default=0.8, gt=0, le=2)
    repetitionPenalty: float = Field(default=1.2, ge=1, le=2)
    topP: float = Field(default=1.0, gt=0, le=1)
    exaggeration: float = Field(default=0.25, ge=0, le=2)
    cfgWeight: float = Field(default=0.5, ge=0, le=1)
    minP: float = Field(default=0.05, ge=0, le=1)
    outputGain: float = Field(default=0.5, gt=0, le=1)


@app.get("/health")
def health():
    return {
        "status": "ok" if model is not None else "loading",
        "engine": "chatterbox-multilingual-v3",
        "cuda": torch.cuda.is_available(),
    }


@app.post("/tts")
def synthesize(request: TtsRequest):
    if model is None:
        raise HTTPException(status_code=503, detail="The model is not loaded")
    if request.language not in model.get_supported_languages():
        raise HTTPException(status_code=400, detail="Unsupported synthesis language")
    if not Path(request.reference).is_file():
        raise HTTPException(status_code=400, detail="Reference WAV not found")
    with synthesis_lock:
        try:
            tokens = model.tokenizer.text_to_tokens(request.text, language_id=request.language)
            if tokens.shape[-1] + 2 >= model.t3.hp.max_text_tokens:
                raise HTTPException(status_code=400, detail="Split the text into shorter segments")
            wav = model.generate(
                request.text,
                language_id=request.language,
                audio_prompt_path=request.reference,
                exaggeration=request.exaggeration,
                cfg_weight=request.cfgWeight,
                temperature=request.temperature,
                repetition_penalty=request.repetitionPenalty,
                min_p=request.minP,
                top_p=request.topP,
            )
            audio = wav.detach().cpu().numpy().reshape(-1) * request.outputGain
            if not len(audio) or not np.isfinite(audio).all() or np.max(np.abs(audio)) >= 1:
                raise RuntimeError("Invalid or clipping synthesis output")
            buffer = io.BytesIO()
            sf.write(buffer, audio, model.sr, format="WAV", subtype="PCM_16")
            return Response(content=buffer.getvalue(), media_type="audio/wav")
        except HTTPException:
            raise
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc

from __future__ import annotations

import io
import warnings
from contextlib import asynccontextmanager
from pathlib import Path

# Bruit provenant de dépendances amont, sans incidence sur Chatterbox-Turbo.
warnings.filterwarnings(
    "ignore",
    message="pkg_resources is deprecated as an API.*",
    category=UserWarning,
)
warnings.filterwarnings(
    "ignore",
    message="`LoRACompatibleLinear` is deprecated.*",
    category=FutureWarning,
)

import soundfile as sf
import torch
from chatterbox.tts_turbo import ChatterboxTurboTTS
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response
from pydantic import BaseModel, Field


model: ChatterboxTurboTTS | None = None


@asynccontextmanager
async def lifespan(_: FastAPI):
    global model
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA is required by the configured LocalDub voice service")
    model = ChatterboxTurboTTS.from_pretrained(device="cuda")
    yield
    model = None
    torch.cuda.empty_cache()


app = FastAPI(title="LocalDub Chatterbox service", lifespan=lifespan)


class TtsRequest(BaseModel):
    text: str = Field(min_length=1, max_length=4000)
    language: str = "en"
    reference: str | None = None
    temperature: float = Field(default=0.75, gt=0, le=2)
    repetitionPenalty: float = Field(default=1.2, ge=1, le=2)
    topP: float = Field(default=0.95, gt=0, le=1)
    topK: int = Field(default=1000, ge=1, le=2000)


@app.get("/health")
def health() -> dict[str, object]:
    return {
        "status": "ok" if model is not None else "loading",
        "engine": "chatterbox-turbo",
        "cuda": torch.cuda.is_available(),
        "device": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
    }


@app.post("/tts")
def synthesize(request: TtsRequest) -> Response:
    if model is None:
        raise HTTPException(status_code=503, detail="The model is not loaded")

    reference = request.reference
    if reference and not Path(reference).is_file():
        raise HTTPException(status_code=400, detail=f"Reference audio not found: {reference}")

    try:
        wav = model.generate(
            request.text,
            audio_prompt_path=reference,
            temperature=request.temperature,
            repetition_penalty=request.repetitionPenalty,
            top_p=request.topP,
            top_k=request.topK,
        )
        audio = wav.squeeze().detach().cpu().numpy()
        buffer = io.BytesIO()
        sf.write(buffer, audio, model.sr, format="WAV", subtype="PCM_16")
        return Response(content=buffer.getvalue(), media_type="audio/wav")
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

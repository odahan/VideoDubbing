from pathlib import Path

import numpy as np
import soundfile as sf
from kokoro import KPipeline


OUTPUT_DIRECTORY = Path(__file__).resolve().parents[1] / "voices"
SAMPLE_TEXT = (
    "Welcome to this detailed review. Today we will explore how this instrument sounds, "
    "how it works, and why it may deserve a place in your studio."
)
VOICES = {
    "am_michael": "michael-us.wav",
    "am_adam": "adam-us.wav",
}


def generate_reference(pipeline: KPipeline, voice: str, destination: Path) -> None:
    chunks = [audio for _, _, audio in pipeline(SAMPLE_TEXT, voice=voice, speed=1.0)]
    if not chunks:
        raise RuntimeError(f"Kokoro produced no audio for {voice}")

    audio = np.concatenate(chunks)
    duration = len(audio) / 24000
    if duration <= 5:
        raise RuntimeError(f"Reference {voice} is too short: {duration:.2f}s")

    sf.write(destination, audio, 24000, subtype="PCM_16")
    print(f"Generated {destination.name}: {duration:.2f}s")


OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)
pipeline = KPipeline(lang_code="a")
for voice_id, filename in VOICES.items():
    output = OUTPUT_DIRECTORY / filename
    if output.exists():
        print(f"Reusing {output.name}")
    else:
        generate_reference(pipeline, voice_id, output)

print("Kokoro male voice references ready")

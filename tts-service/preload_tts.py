import torch
from chatterbox.tts_turbo import ChatterboxTurboTTS

if not torch.cuda.is_available():
    raise RuntimeError("CUDA is not available")

ChatterboxTurboTTS.from_pretrained(device="cuda")
print("Chatterbox Turbo model ready")

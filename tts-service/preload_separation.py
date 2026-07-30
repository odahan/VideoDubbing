from demucs.pretrained import get_model
import torchaudio

if "soundfile" not in torchaudio.list_audio_backends():
    raise RuntimeError("The torchaudio SoundFile backend is not available")

get_model("htdemucs")
print("Demucs model ready")

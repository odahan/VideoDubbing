# LocalDub.NET

[Français](README.FR.MD) | **English**

LocalDub.NET is a fully local French-to-US-English video dubbing pipeline for single-speaker videos. Orchestration is written in C#/.NET; FFmpeg, whisper.cpp, Ollama, and Chatterbox run on your computer. No video data is sent to a cloud service.

The source video is never changed. For `demo.mp4`, LocalDub creates `demo-EN.mp4` next to it, together with:

- `demo-EN.wav`: the complete English voice track, aligned from `00:00:00`, 48 kHz/24-bit PCM;
- `demo-EN.srt`: English subtitles;
- `demo-EN.json`: segments, translations, timings, and processing settings.

## User guides

Choose your language:

- [English user guide](src/LocalDub.NET/README.EN.md)
- [Guide utilisateur français](src/LocalDub.NET/README.md)

The separate voice-profile tool also has its own documentation: [LocalDub.Voices](src/LocalDub.Voices/README.md).

## Pipeline

1. Extract a 48 kHz audio master with FFmpeg.
2. Optionally separate voice and accompaniment with Demucs.
3. Downmix speech to 16 kHz mono and transcribe French with whisper.cpp.
4. Translate contextually in batches with OllamaSharp and Microsoft Agent Framework.
5. Synthesize English segments with Chatterbox-Turbo.
6. Rewrite overly long sentences, then apply a normally limited 1.15x speed-up when needed.
7. Place each sentence at its timestamp and produce a WAV exactly as long as the video.
8. Create the SRT and a new `-EN` video without re-encoding its image.

The main translator is unloaded before Chatterbox to free VRAM. Any rewrites then use the lightweight `granite4:3b` model.

## Requirements

- 64-bit Windows;
- .NET 10 SDK;
- NVIDIA GPU with a recent driver;
- Ollama installed and running;
- about 20–30 GB free for tools, environments, and models.

The supplied configuration uses `gemma4:12b`. You can select another Ollama model in `appsettings.json` or with `--model`.

## Automatic setup

From the repository root:

```powershell
dotnet run --project src/LocalDub.NET -- setup
```

This command installs the following under `.localdub/` without changing existing CUDA or Ollama installations:

- FFmpeg;
- CUDA whisper.cpp and `large-v3-turbo`;
- `uv` and three isolated Python 3.11 environments;
- Chatterbox-Turbo and its weights;
- Demucs and its weights;
- Kokoro and two locally generated US male voice references.

Internet access is required for this first setup. Subsequent dubbing runs work offline. Setup can safely be re-run after an interruption: completed tools and models are kept and the process resumes at the first incomplete step.

Verify the installation:

```powershell
dotnet run --project src/LocalDub.NET -- doctor
```

## Choosing a voice

Setup creates two US male references used by Chatterbox-Turbo:

- `michael-us`: deep, professional voice; the default profile;
- `adam-us`: more energetic and assertive voice;
- `narrator-us`: Chatterbox's built-in female voice.

Preview a profile:

```powershell
dotnet run --project src/LocalDub.NET -- voices audition --voice michael-us
```

You can also use a personal reference. A clean 6–10 second French or English recording works well; it should contain one voice only, without music, reverb, or other speakers. Place it in `voices/` and either select it temporarily from the interactive menu or create a reusable profile with the voice manager:

```powershell
dotnet run --project src/LocalDub.Voices
```

## Interactive dubbing

Run LocalDub without arguments to start the guided assistant:

```powershell
dotnet run --project src/LocalDub.NET
```

It asks for the video, output type, audio treatment, voice, glossary, terms to preserve, and Ollama model, then shows the planned output before confirmation. You can prefill just the input path:

```powershell
dotnet run --project src/LocalDub.NET -- dub --input "D:\Videos\demo.mp4"
```

## Automated dubbing

```powershell
dotnet run --project src/LocalDub.NET -- dub `
  --input "D:\Videos\demo.mp4" `
  --audio-mode separate `
  --production video `
  --voice michael-us `
  --voice-variant stable `
  --glossary ai `
  --preserve "SRP,SOLID,DRY,MAF,Semantic Kernel" `
  --model gemma4:12b `
  --yes
```

An existing `-EN` output prevents a run. `--overwrite` allows replacing that output only; the source video remains protected. Use `--production wav` to create only the aligned English WAV, SRT, and JSON without rebuilding a video.

## Audio modes

### `separate`

Demucs estimates vocal and accompaniment tracks, removes the French voice, then adds the English voice. This is usually best for spoken videos with background music. Separation is imperfect: on musical content, vocoders, choirs, or voice-like instruments can be removed too. The stems remain in `output/work/` for review.

### `duck`

The original track is strongly attenuated while English speech plays, then gradually restored. It better preserves musical demonstrations, although a little French voice may remain audible.

### `external-mix`

The new video contains only the English track. The zero-aligned `-EN.wav` can be placed at `00:00:00` in DaVinci Resolve or another editor to rebuild the final mix with the original tracks.

## Glossaries and resuming work

Example glossaries are included:

- `glossaries/ai.json` for software, MAF, and AI;
- `glossaries/synths.json` for music and synthesizers.

Use the filename without its extension with `--glossary`. `--preserve` adds video-specific terms without editing JSON.

Transcripts, stems, and translations are kept in `output/work/`. Running the same video with the same options reuses them; the workspace changes automatically when the input, audio mode, Ollama model, or glossary changes.

## Build and test

```powershell
dotnet restore LocalDub.NET.slnx -m:1
dotnet build LocalDub.NET.slnx --configuration Release -m:1
dotnet test LocalDub.NET.slnx --configuration Release -m:1
```

## V1 limitations

- single speaker;
- French to US English;
- sentence-level timing without visual lip modification;
- separation quality depends on the content;
- automatic setup targets Windows/NVIDIA.

## Licenses and commercial use

The project code may be used separately under its own license. Tools and models retain their respective licenses. Chatterbox and its official weights are announced under the MIT license, as is whisper.cpp. Always review the licenses of the Ollama models and voice references you use before commercial publication.

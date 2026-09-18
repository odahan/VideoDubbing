# LocalDub.NET User Guide

[Français](README.md) | **English**

LocalDub.NET translates and dubs French videos into US English locally. It always keeps the source video unchanged: generated files use the `-EN` suffix.

This guide covers the main dubbing application. To manage personal voice profiles, see the [voice manager guide](../LocalDub.Voices/README.md).

## Before you start

You need 64-bit Windows, the .NET 10 SDK, a compatible NVIDIA GPU, Ollama installed and running, and a video with an audio track.

From the repository root, prepare the required components once:

```powershell
dotnet run --project src/LocalDub.NET -- setup
```

The setup downloads local tools and models, so it can take some time and needs Internet access. Later dubbing runs remain local. Then verify the installation:

```powershell
dotnet run --project src/LocalDub.NET -- doctor
```

## Guided dubbing

From the repository root, run:

```powershell
dotnet run --project src/LocalDub.NET
```

The assistant asks for, in order:

1. the source video;
2. full translated video or translated WAV only;
3. original soundtrack treatment;
4. voice profile and reference WAV;
5. neutral, stable, or expressive voice variant;
6. glossary and terms to preserve;
7. Ollama translation model.

It displays a summary before running. Reply `o` to confirm, or anything else to cancel.

### Accepted paths

Video and WAV paths can be absolute (for example `D:\Videos\presentation.mp4`) or relative to the folder where you run the command (for example `videos\presentation.mp4`). Quote paths containing spaces on the command line.

## Choose the output

### Full production — translated video

Creates a new `-EN` video with the original image and a new English audio track. The WAV, subtitles, and manifest are created as well. Choose this for a video ready to watch or publish.

### Translated WAV only

Creates the synchronized English WAV, subtitles, and manifest, but does not rebuild a final video. Choose this when you will mix or edit in DaVinci Resolve, Premiere, or another editor. The WAV starts at `00:00:00` and has the same duration as the source video.

## Choose soundtrack treatment

### Separate the French voice and keep the music

Recommended for speech over background music. LocalDub attempts to remove the French voice while preserving accompaniment, then adds the English voice. On musical content, singing, vocoders, or voice-like sounds can be separated incorrectly, so review the output before publishing.

### Lower the original soundtrack during English speech

The original audio remains, but its level drops while the English voice speaks. This is often safer for musical demonstrations, at the cost of possible residual French voice.

### English voice only for external mixing

The generated video contains only the English voice. Use `-EN.wav` in your editor to create the final mix.

## Choose a voice

Built-in profiles include Michael, Adam, and the built-in Chatterbox voice. Create your own profile with:

```powershell
dotnet run --project src/LocalDub.Voices
```

After choosing a profile, the main menu also lists WAV files detected in `voices`, so you can use a personal WAV temporarily without immediately creating a JSON profile.

- **Neutral** uses the profile settings and is the default choice.
- **Stable** gives more regular, predictable narration.
- **Expressive** gives a livelier result, with occasional lower consistency.

## Generated files

For `D:\Videos\demo.mp4`, LocalDub writes alongside the source:

```text
demo-EN.mp4   translated video; full production only
demo-EN.wav   synchronized English voice
demo-EN.srt   English subtitles
demo-EN.json  segments, translations, timings, and selected settings
```

Resume files are stored in `output/work/`, allowing interrupted work to continue faster. Existing output blocks a run by default; the menu asks before replacing it. The source video is never replaced.

## Run without the menu

Example for a full video:

```powershell
dotnet run --project src/LocalDub.NET -- dub `
  --input "D:\Videos\demo.mp4" `
  --production video `
  --audio-mode separate `
  --voice michael-us `
  --voice-variant stable `
  --glossary ai `
  --preserve "SRP,SOLID,Semantic Kernel" `
  --model qwen38OD:latest `
  --yes
```

For the synchronized WAV only, set `--production wav`. Add `--overwrite` to replace existing `-EN` files; it can never change the source video.

## Quick troubleshooting

- Run `doctor` if a tool or environment appears missing.
- Ensure Ollama is running and the chosen model is available.
- Ensure the voice-reference WAV still exists in `voices`.
- For very musical videos, try the ducking mode or export the WAV only and remix in your usual editor.
- After an interruption, repeat the same job to reuse completed transcripts and segments.

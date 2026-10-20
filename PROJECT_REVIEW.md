# Project review — 2026-10-20

## Runtime and model configuration

- Both applications and the test project target `net10.0`.
- `global.json` requests .NET SDK 10.0.302 with `latestFeature` roll-forward; the validated SDK is 10.0.401. This does not roll back to .NET 8 or 9.
- Main translator: `gemma4:12b`, confirmed installed and used for real translation.
- Timing editor: `granite4.2:3b`, confirmed installed and used for a real timing rewrite.
- Local Ollama version: 0.40.6.
- GPU: NVIDIA GeForce RTX 4080, 16 GB VRAM.
- Chatterbox and Demucs: PyTorch 2.6.0+cu124, CUDA available.
- FFmpeg and FFprobe: 8.1.2 from the existing PATH installation.

## Corrections

1. Replaced the obsolete main model name in configuration, C# defaults, and English/French guides.
2. Centralized model discovery, checking, downloading, and unloading through the configured Ollama HTTP endpoint. These operations no longer use a potentially different CLI `OLLAMA_HOST`.
3. Released the timing model before returning to speech synthesis when `UnloadBeforeTts` is enabled.
4. Passed glossary terms to timing rewrites, rejected empty rewrites, and requested complete grammatical sentences.
5. Validated all translation IDs and nonempty text before applying a batch, preventing partially applied invalid batches.
6. Generated subtitles after timing rewrites so subtitles and final manifest use the final narration text.
7. Enforced the next segment's start as the audio boundary, including overlapping source subtitles. Invalid timestamps fail before synthesis.
8. Stopped rendering when a segment cannot fit below the absolute speed limit, rather than silently trimming spoken words. Speed changes above 2x use chained `atempo` filters.
9. Included transcription, timing, audio, voice, and reference-audio content in cache identity. Saved segment JSON through a temporary file before replacement.
10. Bounded Whisper transcription chunks with `-ml` and `-sow`, configurable through `Whisper.MaxSegmentCharacters`.
11. Rejected source media without audio or a positive finite duration.
12. Required a matching, ready CUDA Chatterbox health response and disposed owned process handles after exit.
13. Extended `doctor` beyond file existence to executable, package-import, and CUDA checks.
14. Repaired setup discovery for whisper.cpp releases without Windows assets: search stable versioned releases for a CUDA x64 archive. Verified the upstream release listing contains suitable v1.9.2 archives; the latest v1.9.5 publication has no assets. Existing working installations are kept.
15. Required both FFmpeg and FFprobe before skipping setup, and computed download hashes by streaming rather than loading entire model downloads into RAM.

## Validation

```powershell
dotnet build LocalDub.NET.slnx --configuration Release -m:1
dotnet test LocalDub.NET.slnx --configuration Release --no-build -m:1 --blame-hang-timeout 60s
dotnet list LocalDub.NET.slnx package --vulnerable --include-transitive
dotnet src/LocalDub.NET/bin/Release/net10.0/localdub.dll doctor
```

- Release build: passed, zero warnings and errors.
- Automated tests: 33 passed, zero failed or skipped. Added regression coverage for malformed translation batches, configured model endpoints, timing-model selection and unloading, and segment boundaries.
- NuGet vulnerability check: no known vulnerable packages reported for any project, including transitive packages, using the configured sources on the review date.
- Doctor: all checks passed against this machine's installation.
- CLI help and voice manager startup/clean exit: passed.
- Real 16-second smoke video created from an existing French audio work file, with a neutral generated background image.
- Full video production completed separately in `external-mix`, `separate`, and `duck` modes with the `michael-us` stable voice and the AI glossary.
- WAV-only production also completed successfully with the final build, producing WAV, SRT, and JSON without a new video. Output is under `output/revision-smoke/wav-only/`.
- Gemma translated the audio transcribed by whisper.cpp; Granite shortened an overlong segment in the first full run; Chatterbox generated the voice; FFmpeg assembled the timeline and output video.
- Output metadata verified: video and WAV each exactly 16.000 seconds; WAV is stereo PCM 48 kHz / 24 bits; video is H.264 with AAC audio.
- Verified each final manifest translation is present in its SRT.

The video smoke output is `output/revision-smoke/source-EN.mp4`, with matching WAV, SRT, and JSON. These generated artifacts and caches remain ignored by Git.

## Practical limits of this review

This establishes local execution on the installed Windows/NVIDIA environment and a short French speech sample. It does not certify translation, pronunciation, source separation quality, or lip synchronization for every video. A long production video and representative music backgrounds still deserve listening and subtitle review before publishing.

The complete setup was not reinstalled from scratch: existing working Python environments, model weights, tools, and voice references were preserved. Release discovery was checked against the [official whisper.cpp release API](https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=100). No forced package upgrades were required for the tested pipeline.

The existing product scope remains a single speaker, French to US English. Extreme timing failures now stop explicitly; edit the indicated segment's translation in the corresponding work directory's `segments.json` before retrying. The strengthened cache identity starts fresh work directories for older runs.

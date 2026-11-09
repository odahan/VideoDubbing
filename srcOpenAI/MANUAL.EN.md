# LocalDub OpenAI user manual

**Version 10.0.0** · [Français](MANUAL.FR.md) · [Edition README](README.md)

## Installation

Use Windows x64, the .NET 10 SDK, and an NVIDIA GPU with a recent driver. You also need a
ChatGPT account that can authorize plan usage and an Internet connection for sign-in and text
requests. The edition uses FFmpeg, whisper.cpp, Demucs, and Chatterbox from the existing
`.localdub/` installation at the repository root. If that installation is ready, proceed directly
to the launcher. Otherwise, run the standard audio setup from the root of the repository.

```powershell
dotnet run --project .\src\LocalDub.NET -- setup
```

The standard setup prepares Chatterbox-Turbo and its default voice profiles. A custom
Chatterbox Multilingual V3 profile requires its own local environment, model assets, and reference
WAV. Select an English voice for this edition. Keep personal reference recordings on your
machine and run the two dubbing editions one at a time, since they use the same local TTS ports.

## Connect and choose models

Open `RunMenuWithOpenAI.BAT` at the repository root. If there is no active connection, accept
the sign-in prompt or choose **Connect to ChatGPT** in the menu. Follow **Continue with ChatGPT**
in the browser and grant the application permission to use your plan. A successful connection
is kept in Windows-protected storage for the current user.

**Choose models** displays the models offered by the connected account. The translation model
handles the full French text. The timing model handles shortening and adjacent wording changes.
You can use the same model for both or choose a different timing model. The menu prefers
`gpt-6-luna` when available, and your selections apply until you close the menu.

**Reset ChatGPT connection** removes the saved local registration and credentials and attempts
remote revocation. To change accounts, reset the connection, then sign in with the other account.
OpenAI receives the French narration, English wording when a rewrite is needed, glossary terms
and timing constraints. The media and voice reference WAVs stay on the machine.

## Dub a video

1. Choose **Dub a video** and enter the source video path. The path may be quoted.
2. Choose **Complete video, WAV and subtitles** or **Dubbed WAV and subtitles**.
3. Choose an audio mode by number. The selection also determines which audio track Whisper uses when a transcription is needed.
4. Enter the base narration speed for this video. Press Enter for `1.00`, or enter `1.10` for footage already sped up by 10%. Both decimal dots and decimal commas are accepted.
5. Select the voice, reference and tuning variant. **Neutral** uses the profile’s own tuning. **Stable** and **Expressive** select presets.
6. Select a glossary from the numbered list, **No glossary**, or enter a custom JSON path. Enter any additional spellings to preserve, separated by commas, such as `VST, BBCUT, LIVECUT`.
7. Leave **Existing LocalDub JSON for comparison** blank for a fresh transcription. See the comparison procedure below to reuse a French transcript.
8. Confirm whether to replace earlier OpenAI exports, then confirm the run. Review the paths printed at completion.

| Audio mode | Source for local transcription | Audio in the exported video |
| --- | --- | --- |
| 1. Separate | Vocals isolated by Demucs | Dubbed voice plus the accompaniment isolated from the original |
| 2. Duck | Original soundtrack | Dubbed voice over the original soundtrack, lowered during narration |
| 3. External mix | Original soundtrack | English voice alone for mixing in an editor |

The standalone WAV contains the English voice in all three modes. For an existing edit, use
**Dubbed WAV and subtitles** with **External mix** and place the WAV at the start of the source
timeline. It includes the original lead-in and spans the video duration.

## Phrasing, timing, and captions

The edition joins fragments of the same French sentence before translating them. When Whisper
word timings match the source text, they can anchor a new sentence inside an SRT cue. Otherwise,
the cue stays together. Long blocks are split at a usable clause or cue boundary when needed.
Chatterbox synthesizes each resulting unit in one take.

The base speed applies to every clip. The default preferred allowance is 15% relative to that
base, so a base of `1.10` starts timing rewrites above approximately `1.265` total. The pipeline
can use additional speed after rewriting if the unit still exceeds its time budget. It stops
with the unit ID if the configured absolute limit would be exceeded. Time left over after
speech stays after the unit. Gaps between source units remain on the timeline.

Long English subtitles are split after synthesis. The internal subtitle timings are estimated
from text length within the rendered speech window. The subtitle breaks have no effect on the
voice. The `VST` family keeps its written form in the exports and has a separate pronunciation
for Chatterbox.

## Compare with an earlier render

For the same French input on both runs, enter the path to an earlier `-EN.json` manifest or a
saved `segments.json` at the comparison prompt. The edition imports the French text and timings,
then translates the grouped narration again. It can run Whisper locally to recover word timings
within imported cues. Suspicious loops in the French cues can also trigger a local verification
pass before grouping.

For a useful listening comparison, keep the source video, voice profile, reference WAV,
glossary, base speed and audio mode consistent. The OpenAI edition writes a separate set of
`-EN-OpenAI` files.

## Outputs and recovery

For `demo.mp4`, look beside the input for `demo-EN-OpenAI.wav`, `demo-EN-OpenAI.srt`, and
`demo-EN-OpenAI.json`. Complete video mode also writes `demo-EN-OpenAI.mp4`. The manifest includes
the model choices, source cue IDs, source and voiced time windows, synthesized durations and
applied playback speeds.

The work cache is under `output/openai/work/`. Successful translation batches are saved there.
Rerun with the same settings to reuse them after an interruption. Changing the model, glossary,
reference or base speed can select a new work folder. If fitting fails, inspect the printed
unit ID in the work folder’s `segments.json`. You can edit its English translation there and
retry with the same settings.

| Symptom | What to check |
| --- | --- |
| Shared tool or model missing | Run **Check shared audio installation**. For standard profiles, run the standard setup if files are missing. |
| Sign-in or plan permission missing | Complete the browser flow and allow plan usage. Reset the local connection if you need to switch accounts. |
| Model unavailable or quota reached | Use **Choose models** for the current catalogue and check ChatGPT usage before resuming. |
| An output file already exists | Approve replacement of the earlier OpenAI export, or move it first to keep both renders. |
| OpenAI returns an unexpected response | Record the console error and any displayed request ID. Keep account credentials private. |

## Command-line use

Run these examples from the repository root. Sign in before making a dubbing request. The model
slugs for `--model` and `--timing-model` come from the `models` command.

```powershell
dotnet run --project .\srcOpenAI -- --version
dotnet run --project .\srcOpenAI -- doctor
dotnet run --project .\srcOpenAI -- sign-in
dotnet run --project .\srcOpenAI -- models
dotnet run --project .\srcOpenAI -- dub --input "demo.mp4" --production wav --audio-mode external-mix --base-speed 1.10 --glossary synths --preserve "VST,BBCUT"
dotnet run --project .\srcOpenAI -- dub --input "demo.mp4" --transcript "demo-EN.json" --production wav --audio-mode external-mix
dotnet run --project .\srcOpenAI -- sign-out
```

Use `--help` to see all options. Use `--shared-root` when running the built executable outside
the repository and automatic discovery cannot find the shared installation.

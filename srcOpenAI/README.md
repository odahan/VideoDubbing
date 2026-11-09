# LocalDub OpenAI 10.0.0

**English** | [Français](README.FR.md)

[English user manual](MANUAL.EN.md) · [Manuel en français](MANUAL.FR.md) · [Repository README](../README.md)

LocalDub OpenAI dubs French videos into US English. It uses a connected ChatGPT account for
translation and timing rewrites, and shares the local audio installation with LocalDub.NET.

Run **RunMenuWithOpenAI.BAT** from the repository root on Windows with the .NET 10 SDK installed.
Choose **Continue with ChatGPT** when the browser opens and allow the application to use your plan.
**Choose models** loads the signed-in account's catalogue for translation and timing. You can change
either choice while connected. The menu prefers GPT-6 Luna when the account offers it and keeps
your selection until the menu exits. **Reset ChatGPT connection** removes this application's
protected Windows credentials and registration and attempts to revoke the token with OpenAI.

This edition uses the existing FFmpeg, whisper.cpp, Demucs and Chatterbox installation under the
repository root. Run the local and OpenAI editions one at a time so they can use the same Chatterbox
service ports. Each edition has its own configuration, work cache and export names. The OpenAI
edition writes `-EN-OpenAI` files beside the input and keeps intermediate files in `output/openai/work`.
The exported JSON includes the duration of each synthesized clip and the playback speed applied.
French subtitle fragments are joined into narration units before translation and synthesis. Word
timings anchor sentence starts inside a cue when the words can be matched reliably. Otherwise the
cue stays together. The manifest records the source cue IDs and the estimated end of each voiced
unit. The English SRT wraps and splits long text within each spoken duration. The timing of those
subtitle splits is estimated from the amount of text and has no effect on the voice.

For footage that was sped up before dubbing, set **Base narration speed** for that video.
Enter `1.10` for footage already accelerated by 10%, or leave it at `1.00` for the original
behavior. The base speed applies to every clip. The normal timing allowance is relative to that
speed, and the safety ceiling applies to the total speed. In command-line mode, use
`--base-speed 1.10`.

OpenAI receives source text, translations for timing rewrites, glossary terms and timing constraints.
Audio, video and voice references are processed on the machine. The requests use the signed-in
ChatGPT plan and its usage limits.

## Compare an existing render

In the menu, choose the same voice and audio options as the existing render and enter the existing
`-EN.json` manifest at the transcript prompt. You can also pass the cached `segments.json`.
The glossary menu lists the JSON dictionaries in the shared glossaries folder. Choose a number,
select **No glossary**, or enter a custom JSON path.
The import uses the French text, segment IDs and timing. The existing English translations are
discarded from the imported copy so the OpenAI model translates the same input afresh.

For command-line use, run the following from the repository root after signing in through the menu.

```powershell
dotnet run --project srcOpenAI -- models
dotnet run --project srcOpenAI -- dub --input "source.mp4" --transcript "source-EN.json" --production wav
```

Use `--model` and `--timing-model` with slugs listed by `models` to override model selection. Run
`--help` for other options.

The account registration, loopback callback and Windows vault were adapted from Olivier Dahan's
[TextAid](https://github.com/odahan/TextAid) under its [CC BY-NC 4.0 license](https://github.com/odahan/TextAid/blob/main/Licence.md).
The adaptation uses its own application identity and credential directory and adds a CLI session
lock and connection reset.

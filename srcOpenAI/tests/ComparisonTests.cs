using System.Text;
using LocalDub.Models;
using LocalDub.OpenAI;
using LocalDub.Services;
using Xunit;

namespace LocalDub.OpenAI.Tests;

public sealed class ComparisonTests
{
    [Fact]
    public void LongSubtitlesStayInsideTheSpeechWindowAndPreserveAllWords()
    {
        const string text = "We can do the same thing with a bass line, or use a percussion loop and listen to how the accents change across the whole sequence.";
        var subtitles = NarrationSubtitleLayout.Split([
            new DubSegment { StartMilliseconds = 1200, EndMilliseconds = 6800, Translation = text }
        ]).ToArray();

        Assert.Equal(2, subtitles.Length);
        Assert.Equal(1200, subtitles[0].StartMilliseconds);
        Assert.Equal(subtitles[0].EndMilliseconds, subtitles[1].StartMilliseconds);
        Assert.Equal(6800, subtitles[1].EndMilliseconds);
        Assert.All(subtitles, subtitle => Assert.InRange(subtitle.Translation.Split(Environment.NewLine).Length, 1, 2));
        Assert.Equal(text, string.Join(' ', subtitles.Select(subtitle => subtitle.Translation.Replace(Environment.NewLine, " "))));
    }

    [Fact]
    public void NarrationUsesWordTimingAndMergesFragmentsAcrossCueBoundaries()
    {
        DubSegment[] cues =
        [
            new() { Id = 7, StartMilliseconds = 1000, EndMilliseconds = 3200, SourceText = "C'est gratuit. On peut" },
            new() { Id = 8, StartMilliseconds = 3200, EndMilliseconds = 4600, SourceText = "faire des essais, etc. avec un synthé." },
            new() { Id = 9, StartMilliseconds = 9000, EndMilliseconds = 10300, SourceText = "On reprend." }
        ];
        TimedSourceWord[] words =
        [
            new("C", 1000, 1050), new("est", 1050, 1240), new("gratuit", 1240, 1670),
            new("On", 2070, 2290), new("peut", 2290, 2800)
        ];

        var units = NarrationSegmenter.Group(cues, words);
        Assert.Equal(3, units.Count);
        Assert.Equal(2070, units[0].EndMilliseconds);
        Assert.Equal(2070, units[1].StartMilliseconds);
        Assert.Equal("On peut faire des essais, etc. avec un synthé.", units[1].SourceText);
        Assert.Equal(new[] { 7, 8 }, units[1].SourceCueIds);
        Assert.Equal(4.6 - 2.07 - 0.02, NarrationSegmenter.GetSpeechBudget(units, 1, 11), 6);
        Assert.Equal(9000, units[2].StartMilliseconds);
    }

    [Fact]
    public void UnmatchedWordTimingKeepsCueWholeAndDoesNotBridgeADemonstration()
    {
        DubSegment[] cues =
        [
            new() { Id = 1, StartMilliseconds = 0, EndMilliseconds = 3000, SourceText = "Première phrase. Voici" },
            new() { Id = 2, StartMilliseconds = 3000, EndMilliseconds = 6000, SourceText = "la suite" },
            new() { Id = 3, StartMilliseconds = 12000, EndMilliseconds = 14000, SourceText = "et la démonstration est finie." }
        ];
        var units = NarrationSegmenter.Group(cues, [new("Unrelated", 1500, 2000)]);
        Assert.Equal(2, units.Count);
        Assert.Equal("Première phrase. Voici la suite", units[0].SourceText);
        Assert.Equal(6000, units[0].EndMilliseconds);
        Assert.Equal(12000, units[1].StartMilliseconds);
    }

    [Fact]
    public void WhisperSubwordsAreReconstructedBeforeMatchingCues()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {"transcription":[{"tokens":[
              {"text":"[_BEG_]","offsets":{"from":0,"to":0}},
              {"text":" syn","offsets":{"from":1100,"to":1200}},
              {"text":"thé","offsets":{"from":1200,"to":1310}},
              {"text":".","offsets":{"from":1310,"to":1400}},
              {"text":" Puis","offsets":{"from":1500,"to":1650}}
            ]}]}
            """);
        var words = TimedSourceWord.Read(document.RootElement);
        Assert.Equal(new TimedSourceWord("synthé", 1100, 1310), words[0]);
        Assert.Equal(new TimedSourceWord("Puis", 1500, 1650), words[1]);
    }

    [Fact]
    public void ConfirmedSourceLoopKeepsOneTimedCueAndItsTranslation()
    {
        const string full = "On peut faire la même chose sur une séquence, sur une basse.";
        const string shorter = "On peut faire la même chose sur une basse.";
        DubSegment[] original =
        [
            Cue(51, 372940, 389940, full), Cue(52, 389940, 396940, full), Cue(53, 396940, 402940, full),
            Cue(54, 402940, 403940, shorter), Cue(55, 403940, 404940, shorter), Cue(56, 404940, 405940, shorter)
        ];
        original[0].Translation = "We can do the same with a bass line.";

        Assert.Equal((0, 6), Assert.Single(TranscriptRepeatRepair.FindSuspiciousRegions(original)));
        var verified = Assert.Single(TranscriptRepeatRepair.GetConfirmedCues(original,
            [Cue(1, 388100, 393300, full.Replace(",", ""))]));
        Assert.Equal(51, verified.Id);
        Assert.Equal(388100, verified.StartMilliseconds);
        Assert.Equal(original[0].Translation, verified.Translation);
        Assert.Empty(TranscriptRepeatRepair.GetConfirmedCues(original, [Cue(1, 388100, 393300, "Different words.")]));

        static DubSegment Cue(int id, long start, long end, string source) =>
            new() { Id = id, StartMilliseconds = start, EndMilliseconds = end, SourceText = source };
    }

    [Fact]
    public void DuplicateEnglishWordingIgnoresApostropheStyleAndPunctuation()
    {
        Assert.True(OpenAiTranslator.HasSameWording("It’s free.", "It's free!"));
        Assert.False(OpenAiTranslator.HasSameWording("No charge.", "It's free."));
    }

    [Fact]
    public void BaseSpeedAppliesToShortClipsAndRaisesThePreferredTotalWithinTheCeiling()
    {
        var pace = new NarrationPace(1.1, 1.15, 2.5);
        Assert.Equal(1.1, pace.RequiredRatio(4, 5), 6);
        Assert.Equal(1.2, pace.RequiredRatio(6, 5), 6);
        Assert.Equal(1.265, pace.PreferredRatio, 6);
        Assert.Equal(2.5, new NarrationPace(2.4, 1.15, 2.5).PreferredRatio, 6);
    }

    [Fact]
    public void BaseSpeedAcceptsFrenchDecimalsAndRejectsUnsupportedRatios()
    {
        Assert.Equal(1.0, NarrationPace.ParseBaseRatio(null, 2.5));
        Assert.Equal(1.1, NarrationPace.ParseBaseRatio("1,10", 2.5));
        Assert.Equal(1.1, NarrationPace.ParseBaseRatio("1.10", 2.5));
        Assert.Throws<ArgumentException>(() => NarrationPace.ParseBaseRatio("0.9", 2.5));
        Assert.Throws<ArgumentException>(() => NarrationPace.ParseBaseRatio("2.51", 2.5));
    }

    [Theory]
    [InlineData("silence_start: 0\nsilence_end: 5.145812", 10.12, 5146)]
    [InlineData("silence_start: 3\nsilence_end: 5.145812", 10.12, null)]
    [InlineData("silence_start: 0\nsilence_end: 10.12", 10.12, null)]
    public void OpeningMovesOnlyAfterMeasuredLeadingSilence(string ffmpegLog, double scanEnd, int? expected)
    {
        Assert.Equal((long?)expected, InitialSpeechAlignment.ReadLeadingSilenceEnd(ffmpegLog, scanEnd));
    }

    [Fact]
    public void SpeechSpellsTheTechnicalInitialismAtWordBoundaries()
    {
        Assert.Equal("a vee esstee, two vee esstees, vee esstee three and MyVST.",
            SpokenEnglish.ForSynthesis("a VST, two VSTs, VST3 and MyVST."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportKeepsFrenchAndTimingButDropsOldTranslation(bool manifest)
    {
        const string segments = """
            [{"id":28,"startMs":198500,"endMs":204640,"source":"On va parler d'un VST.","translation":"Previous version"}]
            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(manifest ? "{\"segments\":" + segments + "}" : segments));
        var segment = Assert.Single(await DubPipeline.ReadBaselineAsync(input, CancellationToken.None));
        Assert.Equal(28, segment.Id);
        Assert.Equal(198500, segment.StartMilliseconds);
        Assert.Equal(204640, segment.EndMilliseconds);
        Assert.Equal("On va parler d'un VST.", segment.SourceText);
        Assert.Empty(segment.Translation);
    }

    [Fact]
    public void BatchWithDuplicateIdsDoesNotApplyAnyTranslation()
    {
        DubSegment[] batch = [new() { Id = 27 }, new() { Id = 28 }];
        const string invalid = """[{"id":27,"translation":"First"},{"id":27,"translation":"Duplicate"}]""";
        Assert.Throws<InvalidDataException>(() => OpenAiTranslator.ApplyBatch(batch, invalid));
        Assert.All(batch, segment => Assert.Empty(segment.Translation));

        const string valid = """[{"id":28,"translation":"Second"},{"id":27,"translation":"First"}]""";
        OpenAiTranslator.ApplyBatch(batch, valid);
        Assert.Equal("First", batch[0].Translation);
        Assert.Equal("Second", batch[1].Translation);
    }

    [Theory]
    [InlineData("Shorter.", true)]
    [InlineData("Different.", false)]
    [InlineData(null, false)]
    public async Task StreamRequiresCompletedAndConsistentText(string? finalText, bool success)
    {
        var stream = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Shorter.\"}\n\n";
        if (finalText is not null)
        {
            stream += $$$"""
                data: {"type":"response.completed","response":{"status":"completed","output":[{"content":[{"type":"output_text","text":"{{{finalText}}}"}]}]}}
                """ + "\n\n";
        }
        using var reader = new StringReader(stream);
        var result = OpenAiResponsesClient.ReadCompletedTextAsync(reader, CancellationToken.None);
        if (success)
        {
            Assert.Equal("Shorter.", await result);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => result);
        }
    }
}

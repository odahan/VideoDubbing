using LocalDub.Services;

namespace LocalDub.NET.Tests;

public sealed class SrtTests
{
    [Fact]
    public void ParsesMultilineEntries()
    {
        const string srt = "1\n00:00:01,250 --> 00:00:03,500\nBonjour\nle monde\n\n2\n00:00:04,000 --> 00:00:05,000\nSuite\n";
        var result = SrtParser.Parse(srt);

        Assert.Equal(2, result.Count);
        Assert.Equal(1250, result[0].StartMilliseconds);
        Assert.Equal("Bonjour le monde", result[0].SourceText);
    }

    [Fact]
    public void FormatsLongTimestamps()
    {
        Assert.Equal("01:02:03,004", SubtitleGenerator.FormatTimestamp(3_723_004));
    }
}

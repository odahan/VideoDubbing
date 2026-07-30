using LocalDub.Cli;
using LocalDub.Models;

namespace LocalDub.NET.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void NoArgumentsStartsInteractiveDub()
    {
        var result = CliParser.Parse([]);

        Assert.Equal("dub", result.Command);
        Assert.False(result.Has("yes"));
    }

    [Fact]
    public void ParsesAutomatedDubCommand()
    {
        var result = CliParser.Parse(["dub", "--input", "demo.mp4", "--audio-mode", "external-mix", "--yes"]);

        Assert.Equal("dub", result.Command);
        Assert.Equal("demo.mp4", result.Get("input"));
        Assert.Equal(AudioMode.ExternalMix, result.GetAudioMode());
        Assert.True(result.Has("yes"));
    }

    [Fact]
    public void ParsesPreservedTermsOption()
    {
        var result = CliParser.Parse(["dub", "--input", "demo.mp4", "--preserve", "SRP,SOLID,Semantic Kernel"]);

        Assert.Equal("SRP,SOLID,Semantic Kernel", result.Get("preserve"));
    }

    [Theory]
    [InlineData("video", ProductionMode.CompleteVideo)]
    [InlineData("wav", ProductionMode.TranslatedWavOnly)]
    public void ParsesProductionOption(string value, ProductionMode expected)
    {
        var result = CliParser.Parse(["dub", "--input", "demo.mp4", "--production", value]);

        Assert.Equal(expected, result.GetProductionMode());
    }
}

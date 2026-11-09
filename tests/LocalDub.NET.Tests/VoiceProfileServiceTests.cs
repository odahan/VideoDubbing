using LocalDub.Models;
using LocalDub.Services;
using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.NET.Tests;

public sealed class VoiceProfileServiceTests
{
    [Fact]
    public void RuntimeOverridesPreserveMultilingualVoiceIdentity()
    {
        var service = new VoiceProfileService(new AppSettings(), new PathResolver(Path.GetTempPath()));
        var profile = new VoiceProfile
        {
            Id = "od-fr",
            Engine = "chatterbox-multilingual-v3",
            Language = "fr",
            Exaggeration = 0.25,
            CfgWeight = 0.5,
            MinP = 0.05,
            OutputGain = 0.5,
            Temperature = 0.8,
            TopP = 1.0,
            RepetitionPenalty = 1.2
        };

        var adjusted = service.ApplyOverrides(profile, null, "neutral");

        Assert.Equal("chatterbox-multilingual-v3", adjusted.Engine);
        Assert.Equal("fr", adjusted.Language);
        Assert.Equal(0.25, adjusted.Exaggeration);
        Assert.Equal(0.5, adjusted.CfgWeight);
        Assert.Equal(0.05, adjusted.MinP);
        Assert.Equal(0.5, adjusted.OutputGain);
        Assert.Equal(1.0, adjusted.TopP);
    }

    private static VoiceProfile CreateBaseProfile() => new()
    {
        Id = "test-voice",
        DisplayName = "Test Voice",
        Temperature = 0.42,
        TopP = 0.77,
        RepetitionPenalty = 1.5
    };

    [Fact]
    public void NeutralVariantPreservesBaseProfileTuning()
    {
        var profile = CreateBaseProfile();

        var tuning = VoiceProfileService.GetVariantTuning("neutral", profile);

        Assert.Equal(profile.Temperature, tuning.Temperature);
        Assert.Equal(profile.TopP, tuning.TopP);
        Assert.Equal(profile.RepetitionPenalty, tuning.RepetitionPenalty);
    }

    [Fact]
    public void StableVariantReturnsFixedPreset()
    {
        var tuning = VoiceProfileService.GetVariantTuning("stable", CreateBaseProfile());

        Assert.Equal(0.65, tuning.Temperature);
        Assert.Equal(0.90, tuning.TopP);
        Assert.Equal(1.25, tuning.RepetitionPenalty);
    }

    [Fact]
    public void ExpressiveVariantReturnsFixedPreset()
    {
        var tuning = VoiceProfileService.GetVariantTuning("expressive", CreateBaseProfile());

        Assert.Equal(0.95, tuning.Temperature);
        Assert.Equal(0.98, tuning.TopP);
        Assert.Equal(1.10, tuning.RepetitionPenalty);
    }

    [Theory]
    [InlineData("Neutral")]
    [InlineData("STABLE")]
    [InlineData("Expressive")]
    public void VariantNameIsCaseInsensitive(string variant)
    {
        var exception = Record.Exception(() => VoiceProfileService.GetVariantTuning(variant, CreateBaseProfile()));

        Assert.Null(exception);
    }

    [Fact]
    public void UnknownVariantThrows()
    {
        Assert.Throws<ArgumentException>(() => VoiceProfileService.GetVariantTuning("robotic", CreateBaseProfile()));
    }

    [Fact]
    public void StandardVariantsExposesAllThreeNamesInAStableOrder()
    {
        Assert.Equal(["neutral", "stable", "expressive"], VoiceProfileService.StandardVariants);
    }
}

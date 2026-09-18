using LocalDub.Models;
using LocalDub.Services;

namespace LocalDub.NET.Tests;

public sealed class VoiceProfileServiceTests
{
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

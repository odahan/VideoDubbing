using System.Net.Http.Json;
using LocalDub.Configuration;
using LocalDub.Models;

namespace LocalDub.Services;

/// <summary>
/// Synthesizes narration in the selected profile language through its local Chatterbox service.
/// </summary>
public sealed class ChatterboxSynthesizer(
    AppSettings settings,
    VoiceProfileService profiles,
    TtsServiceHost serviceHost,
    IHttpClientFactory httpClientFactory)
{
    /// <summary>Synthesizes <paramref name="text"/> with the given voice profile and writes the resulting WAV to <paramref name="outputWav"/>.</summary>
    public async Task SynthesizeAsync(
        string text,
        string outputWav,
        VoiceProfile profile,
        CancellationToken cancellationToken)
    {
        await serviceHost.EnsureRunningAsync(cancellationToken, profile.Engine);
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(settings.Tts.RequestTimeoutMinutes);
        var request = new
        {
            text,
            language = profile.Language ?? settings.Tts.Language,
            reference = profiles.ResolveReference(profile),
            temperature = profile.Temperature,
            repetitionPenalty = profile.RepetitionPenalty,
            topP = profile.TopP,
            topK = profile.TopK,
            exaggeration = profile.Exaggeration,
            cfgWeight = profile.CfgWeight,
            minP = profile.MinP,
            outputGain = profile.OutputGain
        };
        using var response = await client.PostAsJsonAsync($"{serviceHost.GetServiceUrl(profile.Engine).TrimEnd('/')}/tts", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(outputWav);
        await source.CopyToAsync(target, cancellationToken);
    }

    /// <summary>
    /// Generates one audition preview WAV per standard voice tuning variant (see
    /// <see cref="VoiceProfileService.StandardVariants"/>) so a user can compare them.
    /// </summary>
    public async Task<IReadOnlyList<string>> AuditionAsync(
        string text,
        VoiceProfile profile,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var outputs = new List<string>();
        foreach (var variantName in VoiceProfileService.StandardVariants)
        {
            var tuning = VoiceProfileService.GetVariantTuning(variantName, profile);
            var adjusted = new VoiceProfile
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                Engine = profile.Engine,
                Language = profile.Language,
                Exaggeration = profile.Exaggeration,
                CfgWeight = profile.CfgWeight,
                MinP = profile.MinP,
                OutputGain = profile.OutputGain,
                ReferenceAudio = profile.ReferenceAudio,
                Temperature = tuning.Temperature,
                RepetitionPenalty = tuning.RepetitionPenalty,
                TopP = tuning.TopP,
                TopK = profile.TopK,
                Description = profile.Description
            };
            var output = Path.Combine(outputDirectory, $"{profile.Id}-{variantName}.wav");
            await SynthesizeAsync(text, output, adjusted, cancellationToken);
            outputs.Add(output);
        }

        return outputs;
    }
}

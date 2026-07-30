using System.Net.Http.Json;
using LocalDub.Configuration;
using LocalDub.Models;

namespace LocalDub.Services;

public sealed class ChatterboxSynthesizer(
    AppSettings settings,
    VoiceProfileService profiles,
    TtsServiceHost serviceHost,
    IHttpClientFactory httpClientFactory)
{
    public async Task SynthesizeAsync(
        string text,
        string outputWav,
        VoiceProfile profile,
        CancellationToken cancellationToken)
    {
        await serviceHost.EnsureRunningAsync(cancellationToken);
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(settings.Tts.RequestTimeoutMinutes);
        var request = new
        {
            text,
            language = settings.Tts.Language,
            reference = profiles.ResolveReference(profile),
            temperature = profile.Temperature,
            repetitionPenalty = profile.RepetitionPenalty,
            topP = profile.TopP,
            topK = profile.TopK
        };
        using var response = await client.PostAsJsonAsync($"{settings.Tts.ServiceUrl.TrimEnd('/')}/tts", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(outputWav);
        await source.CopyToAsync(target, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> AuditionAsync(
        string text,
        VoiceProfile profile,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var variants = new[]
        {
            (Name: "neutral", Temperature: profile.Temperature, TopP: profile.TopP, Repetition: profile.RepetitionPenalty),
            (Name: "stable", Temperature: 0.65, TopP: 0.90, Repetition: 1.25),
            (Name: "expressive", Temperature: 0.95, TopP: 0.98, Repetition: 1.10)
        };

        var outputs = new List<string>();
        foreach (var variant in variants)
        {
            var adjusted = new VoiceProfile
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                Engine = profile.Engine,
                ReferenceAudio = profile.ReferenceAudio,
                Temperature = variant.Temperature,
                RepetitionPenalty = variant.Repetition,
                TopP = variant.TopP,
                TopK = profile.TopK,
                Description = profile.Description
            };
            var output = Path.Combine(outputDirectory, $"{profile.Id}-{variant.Name}.wav");
            await SynthesizeAsync(text, output, adjusted, cancellationToken);
            outputs.Add(output);
        }

        return outputs;
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalDub.Configuration;
using LocalDub.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;

namespace LocalDub.Services;

/// <summary>
/// Translates French dub segments into natural US English narration using a local Ollama model,
/// and can rewrite an over-long translation to fit within a target speaking duration.
/// </summary>
public sealed class OllamaTranslator(
    AppSettings settings,
    IHttpClientFactory httpClientFactory,
    ILogger<OllamaTranslator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Translates every segment in <paramref name="segments"/> in place (batches of
    /// <c>Ollama.BatchSize</c>), honoring the optional glossary's mandatory terms and preserved terms.
    /// </summary>
    public async Task TranslateAsync(
        IReadOnlyList<DubSegment> segments,
        Glossary? glossary,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        var model = modelOverride ?? settings.Ollama.Model;
        using var httpClient = CreateHttpClient();
        using var ollama = new OllamaApiClient(httpClient, model);
        IChatClient chatClient = ollama;
        AIAgent agent = chatClient.AsAIAgent(
            name: "LocalDubTranslator",
            instructions: TranslationInstructions);
        var runOptions = CreateRunOptions(
            settings.Ollama.ContextSize,
            maxOutputTokens: Math.Max(512, settings.Ollama.BatchSize * 80));

        logger.LogInformation("Traduction de {Count} segments avec Ollama/{Model}", segments.Count, model);
        foreach (var batch in segments.Chunk(settings.Ollama.BatchSize))
        {
            var prompt = BuildPrompt(batch, glossary);
            try
            {
                var response = await agent.RunAsync<TranslationEnvelope>(
                    prompt,
                    options: runOptions,
                    cancellationToken: cancellationToken);
                ApplyTranslations(batch, response.Result);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Ollama/{model} n'a pas répondu dans le délai de {settings.Ollama.RequestTimeoutMinutes} minutes.",
                    exception);
            }
        }
    }

    /// <summary>
    /// Asks the timing model to rewrite <paramref name="text"/> so that its spoken duration shrinks
    /// from <paramref name="currentDuration"/> towards <paramref name="targetDuration"/>.
    /// </summary>
    public async Task<string> ShortenAsync(
        string text,
        double currentDuration,
        double targetDuration,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(settings.Ollama.TimingModel)
            ? modelOverride ?? settings.Ollama.Model
            : settings.Ollama.TimingModel;
        using var httpClient = CreateHttpClient();
        using var ollama = new OllamaApiClient(httpClient, model);
        IChatClient chatClient = ollama;
        AIAgent agent = chatClient.AsAIAgent(
            name: "LocalDubTimingEditor",
            instructions: "You edit concise US English narration. Obey the maximum word count exactly. Return only the rewritten sentence, with no quotes, word count, or explanation.");
        var runOptions = CreateRunOptions(settings.Ollama.TimingContextSize, maxOutputTokens: 128);
        var ratio = Math.Clamp(targetDuration / currentDuration, 0.35, 0.98);
        var currentWordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var maximumWords = Math.Max(3, (int)Math.Floor(currentWordCount * ratio * 0.9));
        var prompt = $"Rewrite this narration using no more than {maximumWords} words. Preserve the essential meaning and a natural US narration style. Remove secondary wording if necessary. Do not return the original sentence unchanged:\n{text}";
        try
        {
            var response = await agent.RunAsync(
                prompt,
                options: runOptions,
                cancellationToken: cancellationToken);
            return response.Text.Trim().Trim('"');
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Ollama/{model} n'a pas répondu dans le délai de {settings.Ollama.RequestTimeoutMinutes} minutes pendant la reformulation.",
                exception);
        }
    }

    private HttpClient CreateHttpClient()
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(settings.Ollama.Endpoint);
        client.Timeout = TimeSpan.FromMinutes(settings.Ollama.RequestTimeoutMinutes);
        return client;
    }

    private ChatClientAgentRunOptions CreateRunOptions(int contextSize, int maxOutputTokens)
    {
        var chatOptions = new ChatOptions
        {
            Temperature = settings.Ollama.Temperature,
            MaxOutputTokens = maxOutputTokens,
            Reasoning = new ReasoningOptions
            {
                Effort = settings.Ollama.EnableThinking ? ReasoningEffort.Medium : ReasoningEffort.None
            }
        };
        chatOptions.AddOllamaOption(OllamaOption.NumCtx, contextSize);
        return new ChatClientAgentRunOptions(chatOptions);
    }

    private string BuildPrompt(IReadOnlyList<DubSegment> segments, Glossary? glossary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Translate every French segment below into natural US English narration.");
        builder.AppendLine("Preserve IDs and meaning. Adapt idioms naturally. Do not add explanations.");
        builder.AppendLine("Never exceed maxWords for a segment. Prefer concise spoken phrasing over literal wording.");
        builder.AppendLine("Keep product names, model names, acronyms and technical terminology accurate.");

        if (glossary is not null && glossary.Terms.Count > 0)
        {
            builder.AppendLine("Mandatory terminology:");
            foreach (var term in glossary.Terms)
            {
                builder.AppendLine($"- {term.Key} => {term.Value}");
            }
        }

        if (glossary is not null && glossary.Preserve.Count > 0)
        {
            builder.AppendLine("Preserve these terms exactly, including spelling and capitalization:");
            foreach (var term in glossary.Preserve)
            {
                builder.AppendLine($"- {term}");
            }
        }

        builder.AppendLine("Segments:");
        builder.AppendLine(JsonSerializer.Serialize(segments.Select(segment => new
        {
            id = segment.Id,
            durationMs = segment.EndMilliseconds - segment.StartMilliseconds,
            maxWords = Math.Max(
                2,
                (int)Math.Floor(
                    (segment.EndMilliseconds - segment.StartMilliseconds) / 1000d
                    * settings.Tts.TargetWordsPerSecond)),
            source = segment.SourceText
        }), JsonOptions));
        return builder.ToString();
    }

    private static void ApplyTranslations(IReadOnlyList<DubSegment> source, TranslationEnvelope envelope)
    {
        var byId = envelope.Segments.ToDictionary(item => item.Id);
        foreach (var segment in source)
        {
            if (!byId.TryGetValue(segment.Id, out var translated) || string.IsNullOrWhiteSpace(translated.Translation))
            {
                throw new InvalidDataException($"La traduction du segment {segment.Id} est absente.");
            }

            segment.Translation = translated.Translation.Trim();
        }
    }

    private const string TranslationInstructions = """
        You are a professional audiovisual translator specializing in French-to-US-English localization.
        Produce concise, conversational narration suitable for dubbing technology and synthesizer-review videos.
        Your response must strictly match the requested JSON schema.
        """;

    public sealed class TranslationEnvelope
    {
        [JsonPropertyName("segments")]
        public List<TranslatedSegment> Segments { get; init; } = [];
    }

    public sealed class TranslatedSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("translation")]
        public string Translation { get; init; } = string.Empty;
    }
}

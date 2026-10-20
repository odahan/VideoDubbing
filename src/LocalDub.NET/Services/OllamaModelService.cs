using System.Net.Http.Json;
using System.Text.Json;
using LocalDub.Configuration;

namespace LocalDub.Services;

/// <summary>Manages models on the same configured Ollama server used for translation.</summary>
public sealed class OllamaModelService(AppSettings settings, IHttpClientFactory httpClientFactory)
{
    /// <summary>Lists installed model names without depending on the CLI or OLLAMA_HOST.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("models").EnumerateArray()
            .Select(model => model.GetProperty("name").GetString()!)
            .ToList();
    }

    /// <summary>Checks that a model is installed on the configured server.</summary>
    public async Task CheckAsync(string model, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("api/show", new { model }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Downloads a missing model and waits for the complete pull response.</summary>
    public async Task EnsureInstalledAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            await CheckAsync(model, cancellationToken);
            return;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        using var client = CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var response = await client.PostAsJsonAsync("api/pull", new { model, stream = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
        await CheckAsync(model, cancellationToken);
    }

    /// <summary>Unloads a model to release GPU memory before audio processing.</summary>
    public async Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("api/generate", new { model, keep_alive = 0, stream = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(settings.Ollama.Endpoint.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMinutes(settings.Ollama.RequestTimeoutMinutes);
        return client;
    }
}

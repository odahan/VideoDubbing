using System.Net;
using System.Text;
using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalDub.NET.Tests;

public sealed class OllamaTests
{
    [Fact]
    public async Task ModelOperationsUseConfiguredServerAndExplicitUnload()
    {
        var requests = new List<(Uri Uri, string Body)>();
        var factory = new TestHttpClientFactory(async request =>
        {
            requests.Add((request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
            return JsonResponse(request.Method == HttpMethod.Get ? "{\"models\":[{\"name\":\"gemma4:12b\"}]}" : "{}");
        });
        var settings = new AppSettings { Ollama = new() { Endpoint = "http://configured-server:1234" } };
        var service = new OllamaModelService(settings, factory);

        Assert.Equal(["gemma4:12b"], await service.ListAsync(default));
        await service.CheckAsync("gemma4:12b", default);
        await service.UnloadAsync("gemma4:12b", default);

        Assert.All(requests, request => Assert.Equal("configured-server", request.Uri.Host));
        Assert.Equal("/api/show", requests[1].Uri.AbsolutePath);
        using var unload = JsonDocument.Parse(requests[2].Body);
        Assert.Equal("gemma4:12b", unload.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, unload.RootElement.GetProperty("keep_alive").GetInt32());
    }

    [Theory]
    [InlineData("{\"segments\":[{\"id\":1,\"translation\":\"Hello\"},{\"id\":1,\"translation\":\"Again\"}]}")]
    [InlineData("{\"segments\":[{\"id\":1,\"translation\":\"Hello\"},{\"id\":2,\"translation\":\"\"}]}")]
    [InlineData("{\"segments\":[{\"id\":1,\"translation\":\"Hello\"},{\"id\":3,\"translation\":\"Unexpected\"}]}")]
    public async Task InvalidBatchDoesNotPartiallyChangeTranslations(string content)
    {
        var factory = ChatFactory(content);
        var settings = new AppSettings();
        var translator = new OllamaTranslator(settings, factory, new(settings, factory), NullLogger<OllamaTranslator>.Instance);
        DubSegment[] segments =
        [
            new() { Id = 1, EndMilliseconds = 3000, SourceText = "Bonjour" },
            new() { Id = 2, StartMilliseconds = 3000, EndMilliseconds = 6000, SourceText = "Suite" }
        ];

        await Assert.ThrowsAsync<InvalidDataException>(() => translator.TranslateAsync(segments, null, null, default));
        Assert.All(segments, segment => Assert.Empty(segment.Translation));
    }

    [Fact]
    public async Task TimingRewriteUsesGraniteAndReleasesItsMemory()
    {
        var requests = new List<(string Path, string Body)>();
        var factory = new TestHttpClientFactory(async request =>
        {
            requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync()));
            return request.RequestUri.AbsolutePath == "/api/generate"
                ? JsonResponse("{}")
                : JsonResponse(JsonSerializer.Serialize(new
                {
                    model = "granite4.2:3b", created_at = "2026-10-20T12:00:00Z",
                    message = new { role = "assistant", content = "Use Semantic Kernel." }, done = true, done_reason = "stop"
                }));
        });
        var settings = new AppSettings { Ollama = new() { UnloadBeforeTts = true } };
        var translator = new OllamaTranslator(settings, factory, new(settings, factory), NullLogger<OllamaTranslator>.Instance);
        var glossary = new Glossary { Preserve = ["Semantic Kernel"] };

        var result = await translator.ShortenAsync("You should use Semantic Kernel for this application.", 6, 3, null, default, glossary);

        Assert.Equal("Use Semantic Kernel.", result);
        using var chat = JsonDocument.Parse(requests[0].Body);
        Assert.Equal("granite4.2:3b", chat.RootElement.GetProperty("model").GetString());
        Assert.Contains("Preserve the spelling", requests[0].Body);
        Assert.Equal("/api/generate", requests[^1].Path);
        using var unload = JsonDocument.Parse(requests[^1].Body);
        Assert.Equal("granite4.2:3b", unload.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, unload.RootElement.GetProperty("keep_alive").GetInt32());
    }

    [Fact]
    public async Task AppliesStructuredTranslationFromGemma()
    {
        var factory = ChatFactory("{\"segments\":[{\"id\":7,\"translation\":\"Hello and welcome.\"}]}");
        var settings = new AppSettings();
        var translator = new OllamaTranslator(settings, factory, new(settings, factory), NullLogger<OllamaTranslator>.Instance);
        DubSegment[] segments = [new() { Id = 7, EndMilliseconds = 3000, SourceText = "Bonjour et bienvenue." }];

        await translator.TranslateAsync(segments, null, null, default);

        Assert.Equal("Hello and welcome.", segments[0].Translation);
    }

    private static TestHttpClientFactory ChatFactory(string content) => new(_ => Task.FromResult(JsonResponse(
        JsonSerializer.Serialize(new { model = "gemma4:12b", created_at = "2026-10-20T12:00:00Z", message = new { role = "assistant", content }, done = true, done_reason = "stop" }))));

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class TestHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TestHandler(respond));
    }

    private sealed class TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}

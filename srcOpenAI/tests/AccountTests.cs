using System.Net;
using System.Text;
using System.Text.Json;
using LocalDub.Configuration;
using Xunit;

namespace LocalDub.OpenAI.Tests;

public sealed class AccountTests
{
    private const string CompletedResponse = """
        {"object":"response","status":"completed","output":[{"content":[{"type":"output_text","text":"Translated."}]}]}
        """;

    [Theory]
    [InlineData("text/event-stream", true)]
    [InlineData("application/json", true)]
    [InlineData("text/plain", true)]
    [InlineData(null, true)]
    [InlineData("application/json", false)]
    public async Task RequestsUseTheSelectedModelAndChatGptToken(string? contentType, bool eventStream)
    {
        var vault = new MemoryVault();
        using var sessionHttp = new HttpClient(new Handler(_ => throw new InvalidOperationException("Refresh was unexpected.")));
        var account = new OpenAiAccountSession(sessionHttp, vault);
        var responses = new OpenAiResponsesClient(account, new Factory(() => new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "text/event-stream");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var body = document.RootElement;
            Assert.Equal("selected-model", body.GetProperty("model").GetString());
            Assert.False(body.GetProperty("store").GetBoolean());
            Assert.True(body.GetProperty("stream").GetBoolean());
            Assert.Equal("Source text.", body.GetProperty("input")[0].GetProperty("content").GetString());
            var payload = eventStream ? "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":" +
                CompletedResponse + "}\n\n" : CompletedResponse;
            var content = new StringContent(payload, Encoding.UTF8, contentType ?? "text/plain");
            if (contentType is null)
            {
                content.Headers.ContentType = null;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }))), new AppSettings());

        Assert.Equal("Translated.", await responses.CompleteAsync(
            "selected-model", "Translate.", "Source text.", CancellationToken.None));
    }

    [Theory]
    [InlineData("application/json", "{\"object\":\"response\",\"status\":\"incomplete\",\"output\":[]}")]
    [InlineData("text/html", "<html>unexpected response</html>")]
    public async Task UnexpectedResponsesReportSafeDiagnostics(string contentType, string payload)
    {
        var account = new OpenAiAccountSession(new HttpClient(), new MemoryVault());
        var responses = new OpenAiResponsesClient(account, new Factory(() => new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, contentType)
            };
            response.Headers.Add("x-request-id", "req_example");
            return Task.FromResult(response);
        }))), new AppSettings());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            responses.CompleteAsync("selected-model", "Translate.", "Source text.", CancellationToken.None));
        Assert.Contains($"HTTP 200, Content-Type: {contentType}, request ID: req_example", error.Message);
        Assert.DoesNotContain(payload, error.Message);
    }

    [Fact]
    public async Task ResetRemovesTheLocalRegistrationEvenIfRevocationCannotBeConfirmed()
    {
        var vault = new MemoryVault();
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var account = new OpenAiAccountSession(http, vault);
        Assert.False(await account.ResetAsync(CancellationToken.None));
        Assert.Equal("OpenAiDisconnected", account.StatusKey);
        Assert.Null(vault.Value);
    }

    private sealed class MemoryVault : ISecretVault
    {
        public string? Value { get; private set; } = JsonSerializer.Serialize(new
        {
            client_id = "oaiapp_synthetic",
            access_token = "synthetic-token",
            scope = OpenAiAccountProtocol.PlanScope,
            expires_at = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        public void SetSecret(string reference, string secret) => Value = secret;

        public bool TryGetSecret(string reference, out string secret)
        {
            secret = Value ?? string.Empty;
            return Value is not null;
        }

        public void RemoveSecret(string reference) => Value = null;
    }

    private sealed class Factory(Func<HttpClient> create) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => create();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}

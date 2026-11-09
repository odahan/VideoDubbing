using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalDub.Configuration;

namespace LocalDub.OpenAI;

/// <summary>Reads completed text responses authorized by the user's ChatGPT plan.</summary>
public sealed class OpenAiResponsesClient(
    OpenAiAccountSession account, IHttpClientFactory clients, AppSettings settings)
{
    /// <summary>Sends a text-only request with no server-side response storage.</summary>
    public async Task<string> CompleteAsync(string model, string instructions, string input, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(settings.OpenAI.RequestTimeoutMinutes));
        var token = deadline.Token;
        try
        {
            var accessToken = await account.GetAccessTokenAsync(token);
            using var client = clients.CreateClient("OpenAI");
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiAccountProtocol.Resource + "/responses")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    instructions,
                    input = new[] { new { role = "user", content = input } },
                    store = false,
                    stream = true
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await account.RequireReconnectAsync(accessToken);
                throw new OpenAiAccountException("The OpenAI session was rejected. Run sign-in again.");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new OpenAiAccountException("OpenAI usage is limited. Check ChatGPT Settings > Usage, then resume.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new OpenAiAccountException($"OpenAI rejected the request for '{model}' ({DescribeResponse(response)}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            try
            {
                return await ReadResponseAsync(reader, response.Content.Headers.ContentType?.MediaType, token);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"OpenAI returned invalid response data ({DescribeResponse(response)}).", error);
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"{error.Message} ({DescribeResponse(response)}).", error);
            }
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenAI did not complete the request for '{model}' within the configured timeout.", error);
        }
    }

    private static async Task<string> ReadResponseAsync(
        TextReader reader, string? mediaType, CancellationToken cancellationToken)
    {
        switch (mediaType?.ToLowerInvariant())
        {
            case "text/event-stream":
                return await ReadCompletedTextAsync(reader, cancellationToken);
            case null or "application/json" or "application/octet-stream" or "text/plain":
                break;
            default:
                throw new InvalidDataException("OpenAI returned an unexpected content type");
        }

        string? firstLine;
        do
        {
            firstLine = await reader.ReadLineAsync(cancellationToken);
        } while (firstLine is not null && string.IsNullOrWhiteSpace(firstLine));

        if (firstLine is not null && (firstLine.StartsWith("data:", StringComparison.Ordinal) ||
            firstLine.StartsWith("event:", StringComparison.Ordinal) ||
            firstLine.StartsWith(':') || firstLine.StartsWith("id:", StringComparison.Ordinal) ||
            firstLine.StartsWith("retry:", StringComparison.Ordinal)))
        {
            return await ReadCompletedTextAsync(reader, cancellationToken, firstLine);
        }
        if (firstLine?.TrimStart().StartsWith('{') == true)
        {
            using var document = JsonDocument.Parse(firstLine + "\n" + await reader.ReadToEndAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.ValueEquals("response.completed") &&
                root.TryGetProperty("response", out var completed))
            {
                return ReadFinalText(completed, "");
            }
            if (root.TryGetProperty("object", out var kind) && kind.ValueEquals("response"))
            {
                return ReadFinalText(root, "");
            }
            throw new InvalidDataException("OpenAI returned JSON without a completed Responses result");
        }
        throw new InvalidDataException("OpenAI returned neither an event stream nor a completed response");
    }

    private static string DescribeResponse(HttpResponseMessage response)
    {
        var type = response.Content.Headers.ContentType?.MediaType ?? "missing";
        var requestId = response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
        var details = $"HTTP {(int)response.StatusCode}, Content-Type: {type}";
        return requestId is { Length: > 0 and <= 128 } &&
            requestId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? $"{details}, request ID: {requestId}"
            : details;
    }

    /// <summary>Accepts text only after a terminal completion event with a matching final result.</summary>
    internal static async Task<string> ReadCompletedTextAsync(
        TextReader reader, CancellationToken cancellationToken, string? firstLine = null)
    {
        var data = new StringBuilder();
        var received = new StringBuilder();
        for (var line = firstLine ?? await reader.ReadLineAsync(cancellationToken);
             line is not null;
             line = await reader.ReadLineAsync(cancellationToken))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.AppendLine(line[5..].TrimStart());
            }
            if (line.Length != 0 || data.Length == 0)
            {
                continue;
            }

            var payload = data.ToString().Trim();
            data.Clear();
            if (payload == "[DONE]")
            {
                break;
            }
            using var item = JsonDocument.Parse(payload);
            var root = item.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "response.output_text.delta":
                    received.Append(root.GetProperty("delta").GetString());
                    break;
                case "response.refusal.delta":
                case "response.refusal.done":
                case "response.incomplete":
                case "error":
                    throw new InvalidDataException("OpenAI declined or did not complete the text response.");
                case "response.failed":
                    var errorCode = root.TryGetProperty("response", out var failedResponse) &&
                        failedResponse.ValueKind == JsonValueKind.Object &&
                        failedResponse.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("code", out var code) ? code.GetString() : null;
                    throw new OpenAiAccountException(errorCode == "subscription_sharing_usage_limit_exceeded"
                        ? "The ChatGPT plan usage limit was reached. Check ChatGPT Settings > Usage, then resume."
                        : "OpenAI could not complete the text response.");
                case "response.completed":
                    return ReadFinalText(root.GetProperty("response"), received.ToString());
            }
        }
        throw new InvalidDataException("The OpenAI stream ended before completion. The current batch was not saved.");
    }

    private static string ReadFinalText(JsonElement response, string streamed)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("status", out var status) || !status.ValueEquals("completed") ||
            (response.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) ||
            (response.TryGetProperty("incomplete_details", out var incomplete) && incomplete.ValueKind != JsonValueKind.Null) ||
            !response.TryGetProperty("output", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The OpenAI response did not complete.");
        }

        var final = new StringBuilder();
        foreach (var output in items.EnumerateArray())
        {
            if (!output.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var part in content.EnumerateArray())
            {
                switch (part.GetProperty("type").GetString())
                {
                    case "refusal":
                        throw new InvalidDataException("OpenAI declined the text request.");
                    case "output_text":
                        final.Append(part.GetProperty("text").GetString());
                        break;
                }
            }
        }
        var result = final.ToString();
        if (string.IsNullOrWhiteSpace(result) || (streamed.Length > 0 && streamed != result))
        {
            throw new InvalidDataException("The completed OpenAI text is empty or differs from the streamed text.");
        }
        return result;
    }
}

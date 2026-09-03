using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClickBaitThumbnailGenerator;

public interface IOpenAiClient
{
    Task<IReadOnlyList<ScenarioCandidate>> GenerateScenariosAsync(int count, CancellationToken cancellationToken);
    Task<GeneratedImage> GenerateImageAsync(string prompt, CancellationToken cancellationToken);
    Task<GeneratedTitles> GenerateDistractorTitlesAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public sealed class OpenAiClient(HttpClient httpClient, OpenAiOptions options) : IOpenAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ScenarioCandidate>> GenerateScenariosAsync(int count, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = options.ScenarioModel,
            store = false,
            input = $"""
                Generate exactly {count} varied, family-friendly comedy thumbnail scenarios. Balance animal chaos, cooking disasters,
                strange inventions, impossible discoveries, DIY failures, paranormal encounters, travel catastrophes, scale differences,
                bizarre experiments, transformations, underwater situations, parties going wrong, mysterious openings, impossible vehicles,
                ridiculous challenges, before-and-after scenes, exaggerated reactions, and domestic disasters. Each must be immediately visual,
                distinct, and free of celebrities, politicians, real people, brands, copyrighted characters, written text, and unsafe content.
                Vary the grammar and composition. Return only the requested structured result.
                """,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "thumbnail_scenarios",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "scenarios" },
                        properties = new
                        {
                            scenarios = new
                            {
                                type = "array",
                                minItems = count,
                                maxItems = count,
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "scene", "category", "composition", "visualStyle" },
                                    properties = new
                                    {
                                        scene = new { type = "string", minLength = 12, maxLength = 300 },
                                        category = new { type = "string", minLength = 2, maxLength = 80 },
                                        composition = new { type = "string", minLength = 2, maxLength = 80 },
                                        visualStyle = new { type = "string", minLength = 2, maxLength = 80 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        using var response = await SendAsync("responses", request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var outputText = ExtractOutputText(json);
        var envelope = JsonSerializer.Deserialize<ScenarioEnvelope>(outputText, JsonOptions)
            ?? throw new OpenAiRequestException("The scenario response was empty or malformed.");
        return envelope.Scenarios;
    }

    public async Task<GeneratedImage> GenerateImageAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = options.ImageModel,
            prompt,
            size = options.ImageSize,
            quality = options.ImageQuality,
            n = 1
        };

        using var response = await SendAsync("images/generations", request, cancellationToken).ConfigureAwait(false);
        var requestId = response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!json.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            throw new OpenAiRequestException("The image response contained no image data.");

        var item = data[0];
        if (item.TryGetProperty("b64_json", out var encoded) && !string.IsNullOrWhiteSpace(encoded.GetString()))
        {
            try { return new GeneratedImage(Convert.FromBase64String(encoded.GetString()!), requestId); }
            catch (FormatException exception) { throw new OpenAiRequestException("The image response contained invalid base64 data.", innerException: exception); }
        }

        if (item.TryGetProperty("url", out var urlElement) && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri))
        {
            using var download = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!download.IsSuccessStatusCode) throw CreateException(download, "Image download failed.");
            return new GeneratedImage(await download.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), requestId);
        }

        throw new OpenAiRequestException("The image response contained neither base64 data nor a URL.");
    }

    public async Task<GeneratedTitles> GenerateDistractorTitlesAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0) throw new ArgumentException("Image data cannot be empty.", nameof(imageBytes));
        var request = new
        {
            model = options.VisionModel,
            store = false,
            max_output_tokens = 1000,
            reasoning = new { effort = "low" },
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = """
                                Inspect this comedy thumbnail and invent exactly two funny YouTube-style DISTRACTOR titles for a party game.
                                They are plausible alternative interpretations, not a canonical answer and not a literal inventory of the picture.
                                Make each title punchy, family-friendly, distinct from the other, and between three and eight words.
                                Use absurd curiosity or comic jeopardy, like "I Invented Liquid Rainbows" or "Never Put a Ladder in Paint".
                                Do not mention AI, an image, a thumbnail, YouTube, brands, real people, copyrighted characters, or quote marks.
                                Return only the requested structured result.
                                """
                        },
                        new
                        {
                            type = "input_image",
                            image_url = $"data:image/webp;base64,{Convert.ToBase64String(imageBytes)}",
                            detail = "low"
                        }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "thumbnail_distractor_titles",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "titles" },
                        properties = new
                        {
                            titles = new
                            {
                                type = "array",
                                minItems = 2,
                                maxItems = 2,
                                items = new { type = "string", minLength = 3, maxLength = 80 }
                            }
                        }
                    }
                }
            }
        };

        using var response = await SendAsync("responses", request, cancellationToken).ConfigureAwait(false);
        var requestId = response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var outputText = ExtractOutputText(json);
        var envelope = JsonSerializer.Deserialize<TitleEnvelope>(outputText, JsonOptions);
        if (envelope?.Titles is null)
            throw new OpenAiRequestException("The title response was empty or malformed.");
        var titles = envelope.Titles.Select(title => title.Trim()).ToArray();
        if (titles.Length != 2 || titles.Any(string.IsNullOrWhiteSpace) ||
            titles.Any(title => title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length is < 3 or > 8) ||
            string.Equals(ScenarioUtilities.Normalize(titles[0]), ScenarioUtilities.Normalize(titles[1]), StringComparison.Ordinal))
            throw new OpenAiRequestException("The vision model did not return two distinct distractor titles.", transient: true);
        return new GeneratedTitles(titles, requestId);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new OpenAiRequestException("OPENAI_API_KEY is not set. Create a new key and export it before using generation commands.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpenAiRequestException("The OpenAI request timed out.", transient: true, innerException: exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            var exception = await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw exception;
        }

        return response;
    }

    private static string ExtractOutputText(JsonElement response)
    {
        if (response.TryGetProperty("output_text", out var direct) && !string.IsNullOrWhiteSpace(direct.GetString()))
            return direct.GetString()!;
        if (response.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)) continue;
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" && part.TryGetProperty("text", out var text))
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
            }
        }

        var status = response.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        string? reason = null;
        if (response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("reason", out var reasonElement))
            reason = reasonElement.GetString();
        if (!string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(reason))
        {
            var detail = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";
            throw new OpenAiRequestException($"The response ended with status '{status ?? "unknown"}'{detail} before producing structured output.", transient: true);
        }

        throw new OpenAiRequestException("The response contained no structured output text.");
    }

    private static async Task<OpenAiRequestException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = $"OpenAI returned HTTP {(int)response.StatusCode}.";
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (json.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var detail))
                message += $" {detail.GetString()}";
        }
        catch (JsonException) { }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
            retryAfter = date - DateTimeOffset.UtcNow;
        var status = (int)response.StatusCode;
        return new OpenAiRequestException(message, status, retryAfter, response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500);
    }

    private static OpenAiRequestException CreateException(HttpResponseMessage response, string message)
    {
        var status = (int)response.StatusCode;
        return new OpenAiRequestException(message, status, response.Headers.RetryAfter?.Delta, status == 429 || status >= 500);
    }

    private sealed record ScenarioEnvelope(IReadOnlyList<ScenarioCandidate> Scenarios);
    private sealed record TitleEnvelope(IReadOnlyList<string> Titles);
}

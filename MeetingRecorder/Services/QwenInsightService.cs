using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeetingRecorder.Models;

namespace MeetingRecorder.Services;

public class QwenInsightService : IInsightService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;

    public event EventHandler<InsightEventArgs>? InsightGenerated;
    public event EventHandler<string>? StatusChanged;

    public QwenInsightService(HttpClient httpClient, AppSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<string?> AnalyzeAsync(string mentionSnippet, string contextTranscript, string detectedLanguage = "auto", CancellationToken ct = default)
    {
        string apiKey = _settings.DashScopeApiKey?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.WriteLine("[QwenInsight] API key not configured.");
            return null;
        }

        try
        {
            StatusChanged?.Invoke(this, "Analyzing mention with AI...");

            string model = string.IsNullOrWhiteSpace(_settings.QwenModel) ? "qwen-turbo" : _settings.QwenModel.Trim();
            string endpoint = ResolveGenerationEndpoint(_settings.DashScopeBaseUrl);

            string langInstruction = detectedLanguage?.ToLower() switch
            {
                "zh" or "zh-cn" => "Please respond in Simplified Chinese (简体中文).",
                "en" => "Please respond in English.",
                "ja" => "Please respond in Japanese (日本語).",
                "ko" => "Please respond in Korean (한국어).",
                _ => "Respond in the same language as the spoken transcript."
            };

            string systemPrompt = $"You are an executive meeting assistant. The user was mentioned in the meeting conversation. " +
                                  $"Analyze the context and provide a brief, actionable insight (1-2 sentences max) indicating what was asked of them, assigned to them, or said about them. " +
                                  $"{langInstruction} Be direct and concise.";

            string userPrompt = $"[Recent Meeting Transcript Context]\n{contextTranscript}\n\n[Sentence with Mention]\n\"{mentionSnippet}\"\n\nProvide the brief insight for the user:";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            // Build request body supporting DashScope native or OpenAI compatible
            bool isOpenAiEndpoint = endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
            string jsonBody;

            if (isOpenAiEndpoint)
            {
                var openAiPayload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    max_tokens = 300,
                    temperature = 0.7
                };
                jsonBody = JsonSerializer.Serialize(openAiPayload);
            }
            else
            {
                var dashScopePayload = new
                {
                    model = model,
                    input = new
                    {
                        messages = new object[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    parameters = new
                    {
                        result_format = "message",
                        max_tokens = 300,
                        temperature = 0.7
                    }
                };
                jsonBody = JsonSerializer.Serialize(dashScopePayload);
            }

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[QwenInsight] HTTP error {response.StatusCode}: {responseBody}");
                StatusChanged?.Invoke(this, $"AI Insight error: {response.StatusCode}");
                return null;
            }

            string? insightText = ExtractInsightText(responseBody);
            if (!string.IsNullOrWhiteSpace(insightText))
            {
                insightText = insightText.Trim();
                InsightGenerated?.Invoke(this, new InsightEventArgs(insightText, mentionSnippet));
                StatusChanged?.Invoke(this, "AI Insight generated.");
                return insightText;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QwenInsight] Error analyzing mention: {ex.Message}");
            StatusChanged?.Invoke(this, $"AI Insight failed: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> TestConnectionAsync(string apiKey, string baseUrl, string model, CancellationToken ct = default)
    {
        try
        {
            string endpoint = ResolveGenerationEndpoint(baseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            bool isOpenAiEndpoint = endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
            string jsonBody;

            if (isOpenAiEndpoint)
            {
                var openAiPayload = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "qwen-turbo" : model.Trim(),
                    messages = new object[]
                    {
                        new { role = "user", content = "Hi" }
                    },
                    max_tokens = 5
                };
                jsonBody = JsonSerializer.Serialize(openAiPayload);
            }
            else
            {
                var dashScopePayload = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "qwen-turbo" : model.Trim(),
                    input = new
                    {
                        messages = new object[]
                        {
                            new { role = "user", content = "Hi" }
                        }
                    },
                    parameters = new
                    {
                        result_format = "message",
                        max_tokens = 5
                    }
                };
                jsonBody = JsonSerializer.Serialize(dashScopePayload);
            }

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QwenInsight] Test connection failed: {ex.Message}");
            return false;
        }
    }

    private static string? ExtractInsightText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // DashScope Native output: output.choices[0].message.content
            if (root.TryGetProperty("output", out var output))
            {
                if (output.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                    {
                        return content.GetString();
                    }
                }
                else if (output.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString();
                }
            }

            // OpenAI compatible output: choices[0].message.content
            if (root.TryGetProperty("choices", out var openAiChoices) && openAiChoices.GetArrayLength() > 0)
            {
                var first = openAiChoices[0];
                if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QwenInsight] JSON extraction error: {ex.Message}");
        }

        return null;
    }

    public static string ResolveGenerationEndpoint(string? baseUrl)
    {
        string baseStr = baseUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(baseStr))
        {
            baseStr = "https://dashscope.aliyuncs.com";
        }

        baseStr = baseStr.TrimEnd('/');

        if (baseStr.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = "http://" + baseStr.Substring(5);
        }
        else if (baseStr.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = "https://" + baseStr.Substring(6);
        }

        if (baseStr.Contains("/v1/services/aigc/text-generation/generation", StringComparison.OrdinalIgnoreCase) ||
            baseStr.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return baseStr;
        }

        if (baseStr.Contains("dashscope.aliyuncs.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseStr}/api/v1/services/aigc/text-generation/generation";
        }

        // Generic custom OpenAI-compatible endpoint
        return $"{baseStr}/v1/chat/completions";
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface IDashScopePhraseService
{
    Task<string?> CreatePhrasesAsync(string apiKey, string baseUrl, string model, Dictionary<string, int> phrases, CancellationToken ct = default);
}

public class DashScopePhraseService : IDashScopePhraseService
{
    private readonly HttpClient _httpClient;

    public DashScopePhraseService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> CreatePhrasesAsync(string apiKey, string baseUrl, string model, Dictionary<string, int> phrases, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || phrases == null || phrases.Count == 0)
        {
            return null;
        }

        string endpoint = ResolvePhraseEndpoint(baseUrl);
        string targetModel = string.IsNullOrWhiteSpace(model) ? "paraformer-realtime-v1" : model.Trim();
        if (targetModel.Contains("v2", StringComparison.OrdinalIgnoreCase))
        {
            targetModel = "paraformer-realtime-v1"; // Phrase manager compiles for paraformer-realtime-v1 / base models
        }

        var payload = new
        {
            model = targetModel,
            phrases = phrases,
            training_type = "compile_asr_phrase"
        };

        string json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        string responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[DashScopePhrase] Create phrases failed with {response.StatusCode}: {responseBody}");
            throw new InvalidOperationException($"DashScope error ({response.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // Try output.finetuned_output or finetuned_output or job_id
        if (root.TryGetProperty("output", out var output))
        {
            if (output.TryGetProperty("finetuned_output", out var fineTuned) && !string.IsNullOrWhiteSpace(fineTuned.GetString()))
            {
                return fineTuned.GetString();
            }
            if (output.TryGetProperty("job_id", out var jobId) && !string.IsNullOrWhiteSpace(jobId.GetString()))
            {
                return jobId.GetString();
            }
        }

        if (root.TryGetProperty("finetuned_output", out var directFineTuned) && !string.IsNullOrWhiteSpace(directFineTuned.GetString()))
        {
            return directFineTuned.GetString();
        }

        return null;
    }

    public static string ResolvePhraseEndpoint(string? baseUrl)
    {
        string baseStr = baseUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(baseStr))
        {
            baseStr = "https://dashscope.aliyuncs.com";
        }

        baseStr = baseStr.TrimEnd('/');

        // Strip /compatible-mode/v1 or /compatible-mode or /v1
        if (baseStr.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/compatible-mode/v1".Length);
        }
        else if (baseStr.EndsWith("/compatible-mode", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/compatible-mode".Length);
        }
        else if (baseStr.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                 !baseStr.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseStr = baseStr.Substring(0, baseStr.Length - "/v1".Length);
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

        if (baseStr.Contains("/api/v1/services/audio/asr/phrase", StringComparison.OrdinalIgnoreCase))
        {
            return baseStr;
        }

        return $"{baseStr}/api/v1/services/audio/asr/phrase";
    }

    public static Dictionary<string, int> ParseHotwords(string? hotwordsText, IEnumerable<string>? mentionNames = null)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // First add mention names with default weight 5
        if (mentionNames != null)
        {
            foreach (var name in mentionNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string cleanName = name.Trim();
                // If contains alias separator '|', add each sub-part
                var parts = cleanName.Split('|', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    string p = part.Trim();
                    if (!string.IsNullOrWhiteSpace(p) && !dict.ContainsKey(p))
                    {
                        dict[p] = 5;
                    }
                }
            }
        }

        // Then parse custom hotwords text e.g. "张伟:5, Alex:5, 产品部:3" or "张伟, Alex"
        if (!string.IsNullOrWhiteSpace(hotwordsText))
        {
            var entries = hotwordsText.Split(new[] { ',', ';', '\n', '\r', '，', '；' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                string word = trimmed;
                int weight = 5;

                int colonIdx = trimmed.IndexOfAny(new[] { ':', '：' });
                if (colonIdx > 0 && colonIdx < trimmed.Length - 1)
                {
                    word = trimmed.Substring(0, colonIdx).Trim();
                    string weightStr = trimmed.Substring(colonIdx + 1).Trim();
                    if (int.TryParse(weightStr, out int parsedWeight))
                    {
                        weight = Math.Clamp(parsedWeight, -6, 5);
                    }
                }

                if (!string.IsNullOrWhiteSpace(word))
                {
                    dict[word] = weight;
                }
            }
        }

        return dict;
    }
}

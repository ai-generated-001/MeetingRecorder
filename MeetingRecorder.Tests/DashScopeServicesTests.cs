using System;
using FluentAssertions;
using MeetingRecorder.Services;
using Xunit;

namespace MeetingRecorder.Tests;

public class DashScopeServicesTests
{
    [Theory]
    [InlineData("https://dashscope.aliyuncs.com", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("https://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/compatible-mode/v1", "wss://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("http://dashscope.aliyuncs.com", "ws://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("dashscope.aliyuncs.com", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("wss://dashscope.aliyuncs.com/api-ws/v1/inference", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("wss://dashscope.aliyuncs.com/api-ws/v1/inference/", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData("", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    [InlineData(null, "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
    public void GetWebSocketUri_ResolvesCorrectUri(string? input, string expected)
    {
        var uri = DashScopeTranscriptionService.GetWebSocketUri(input);
        uri.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("https://dashscope.aliyuncs.com", "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")]
    [InlineData("https://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/compatible-mode/v1", "https://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions")]
    [InlineData("https://api.openai.com", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://custom-proxy.internal/v1/chat/completions", "https://custom-proxy.internal/v1/chat/completions")]
    [InlineData("", "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation")]
    public void ResolveGenerationEndpoint_ResolvesCorrectUrl(string? input, string expected)
    {
        var result = QwenInsightService.ResolveGenerationEndpoint(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void InsightEventArgs_ConstructsPropertiesCorrectly()
    {
        var args = new InsightEventArgs("Please submit budget report.", "Alex, please submit budget report.");
        args.InsightText.Should().Be("Please submit budget report.");
        args.MentionSnippet.Should().Be("Alex, please submit budget report.");
        args.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("https://dashscope.aliyuncs.com", "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData("https://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/compatible-mode/v1", "https://ws-oyh3eh8d1ea0swra.cn-beijing.maas.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData("http://dashscope.aliyuncs.com", "http://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData("https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase", "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData("", "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    [InlineData(null, "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/phrase")]
    public void ResolvePhraseEndpoint_ResolvesCorrectUrl(string? input, string expected)
    {
        var result = DashScopePhraseService.ResolvePhraseEndpoint(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ParseHotwords_ParsesHotwordsAndMentionsCorrectly()
    {
        var mentions = new[] { "Alex|Alec", "张伟" };
        string hotwords = "李娜:5, ProjectAlpha:4, NegativeWord:-2";

        var dict = DashScopePhraseService.ParseHotwords(hotwords, mentions);

        dict.Should().ContainKey("Alex").WhoseValue.Should().Be(5);
        dict.Should().ContainKey("Alec").WhoseValue.Should().Be(5);
        dict.Should().ContainKey("张伟").WhoseValue.Should().Be(5);
        dict.Should().ContainKey("李娜").WhoseValue.Should().Be(5);
        dict.Should().ContainKey("ProjectAlpha").WhoseValue.Should().Be(4);
        dict.Should().ContainKey("NegativeWord").WhoseValue.Should().Be(-2);
    }
}

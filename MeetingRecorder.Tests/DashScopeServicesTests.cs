using System;
using FluentAssertions;
using MeetingRecorder.Services;
using Xunit;

namespace MeetingRecorder.Tests;

public class DashScopeServicesTests
{
    [Theory]
    [InlineData("https://dashscope.aliyuncs.com", "wss://dashscope.aliyuncs.com/api-ws/v1/inference")]
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
    [InlineData("https://api.openai.com", "https://api.openai.com/v1/chat/completions")]
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
}

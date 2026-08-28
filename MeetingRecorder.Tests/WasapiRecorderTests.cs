using System;
using FluentAssertions;
using NAudio.Wave;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using Xunit;

namespace MeetingRecorder.Tests;

public class WasapiRecorderTests
{
    [Fact]
    public void CalculatePeak_IeeeFloat_DetectsSilenceAndPeak()
    {
        var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        
        // Pure silence
        byte[] silentBuffer = new byte[1024];
        float silentPeak = WasapiRecorder.CalculatePeak(silentBuffer, silentBuffer.Length, floatFormat);
        silentPeak.Should().Be(0f);

        // Buffer with 0.5f signal
        byte[] audioBuffer = new byte[16];
        float[] samples = new[] { 0.1f, -0.5f, 0.3f, -0.2f };
        Buffer.BlockCopy(samples, 0, audioBuffer, 0, 16);
        float audioPeak = WasapiRecorder.CalculatePeak(audioBuffer, 16, floatFormat);
        audioPeak.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void CalculatePeak_Pcm16_DetectsSilenceAndPeak()
    {
        var pcm16Format = new WaveFormat(44100, 16, 2);

        // Pure silence
        byte[] silentBuffer = new byte[1024];
        float silentPeak = WasapiRecorder.CalculatePeak(silentBuffer, silentBuffer.Length, pcm16Format);
        silentPeak.Should().Be(0f);

        // Buffer with half-scale short: 16384 -> ~0.5f
        byte[] audioBuffer = new byte[8];
        short[] samples = new short[] { 100, -16384, 500, 0 };
        Buffer.BlockCopy(samples, 0, audioBuffer, 0, 8);
        float audioPeak = WasapiRecorder.CalculatePeak(audioBuffer, 8, pcm16Format);
        audioPeak.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void CalculatePeak_InvalidOrEmptyBuffer_ReturnsZero()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        WasapiRecorder.CalculatePeak(Array.Empty<byte>(), 0, format).Should().Be(0f);
        WasapiRecorder.CalculatePeak(new byte[10], 0, format).Should().Be(0f);
        WasapiRecorder.CalculatePeak(new byte[10], -5, format).Should().Be(0f);
    }

    [Fact]
    public void WasapiRecorder_CanBeInstantiatedWithOrWithoutSettings()
    {
        var settings = new AppSettings { MicrophoneDeviceId = "non-existent-device" };
        using var recorderWithSettings = new WasapiRecorder(settings);
        recorderWithSettings.IsRecording.Should().BeFalse();

        using var recorderWithoutSettings = new WasapiRecorder();
        recorderWithoutSettings.IsRecording.Should().BeFalse();
    }
}

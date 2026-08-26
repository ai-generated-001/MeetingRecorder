using System;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingRecorder.Models;
using MeetingRecorder.Services;

namespace MeetingRecorder.ViewModels;

public partial class TranscriptionOverlayViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IInsightService _insightService;
    private readonly AppSettings _settings;

    public ObservableCollection<TranscriptionSegment> Segments { get; } = new();

    [ObservableProperty]
    private string _latestText = "";

    [ObservableProperty]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private bool _isPinned = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _insightText = "";

    [ObservableProperty]
    private string _insightSnippet = "";

    [ObservableProperty]
    private bool _isInsightVisible;

    public TranscriptionOverlayViewModel(ITranscriptionService transcriptionService, IInsightService insightService, AppSettings settings)
    {
        _transcriptionService = transcriptionService;
        _insightService = insightService;
        _settings = settings;

        _transcriptionService.SegmentTranscribed += OnSegmentTranscribed;
        _transcriptionService.StatusChanged += OnStatusChanged;
        _insightService.InsightGenerated += OnInsightGenerated;
    }

    private void OnSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Segments.Add(e.Segment);
            LatestText = e.Segment.Text;
        });
    }

    private void OnInsightGenerated(object? sender, InsightEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            InsightText = e.InsightText;
            InsightSnippet = e.MentionSnippet;
            IsInsightVisible = true;
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = status;
        });
    }

    [RelayCommand]
    private void DismissInsight()
    {
        IsInsightVisible = false;
    }

    [RelayCommand]
    private void TogglePin()
    {
        IsPinned = !IsPinned;
    }

    [RelayCommand]
    private void Close()
    {
        IsOverlayVisible = false;
    }
    
    public void Clear()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Segments.Clear();
            LatestText = "";
            InsightText = "";
            InsightSnippet = "";
            IsInsightVisible = false;
            StatusText = "Waiting for audio...";
        });
    }
}

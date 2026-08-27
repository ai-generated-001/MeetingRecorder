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
    [NotifyPropertyChangedFor(nameof(HasLiveText))]
    private string _currentLiveText = "";

    public bool HasLiveText => !string.IsNullOrWhiteSpace(CurrentLiveText);

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

    [ObservableProperty]
    private bool _isAiActive = true;

    [ObservableProperty]
    private string _toggleAiButtonTooltip = Resources.TurnOffAi;

    public event Action? ToggleAiRequested;

    public TranscriptionOverlayViewModel(ITranscriptionService transcriptionService, IInsightService insightService, AppSettings settings)
    {
        _transcriptionService = transcriptionService;
        _insightService = insightService;
        _settings = settings;
        _isAiActive = settings.TranscriptionEnabled;
        _toggleAiButtonTooltip = _isAiActive ? Resources.TurnOffAi : Resources.TurnOnAi;

        _transcriptionService.SegmentTranscribed += OnSegmentTranscribed;
        _transcriptionService.PartialSegmentTranscribed += OnPartialSegmentTranscribed;
        _transcriptionService.StatusChanged += OnStatusChanged;
        _insightService.InsightGenerated += OnInsightGenerated;
    }

    partial void OnIsAiActiveChanged(bool value)
    {
        ToggleAiButtonTooltip = value ? Resources.TurnOffAi : Resources.TurnOnAi;
    }

    [RelayCommand]
    private void ToggleAi()
    {
        ToggleAiRequested?.Invoke();
    }

    private void OnSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            CurrentLiveText = "";
            Segments.Add(e.Segment);
            LatestText = e.Segment.Text;
        });
    }

    private void OnPartialSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            CurrentLiveText = e.Segment.Text;
        });
    }

    private void OnInsightGenerated(object? sender, InsightEventArgs e)
    {
        ExecuteOnUIThread(() =>
        {
            InsightText = e.InsightText;
            InsightSnippet = e.MentionSnippet;
            IsInsightVisible = true;
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        ExecuteOnUIThread(() =>
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
        ExecuteOnUIThread(() =>
        {
            Segments.Clear();
            LatestText = "";
            CurrentLiveText = "";
            InsightText = "";
            InsightSnippet = "";
            IsInsightVisible = false;
            StatusText = "Waiting for audio...";
        });
    }

    private void ExecuteOnUIThread(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}

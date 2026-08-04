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

    public TranscriptionOverlayViewModel(ITranscriptionService transcriptionService, AppSettings settings)
    {
        _transcriptionService = transcriptionService;
        _settings = settings;

        _transcriptionService.SegmentTranscribed += OnSegmentTranscribed;
        _transcriptionService.StatusChanged += OnStatusChanged;
    }

    private void OnSegmentTranscribed(object? sender, TranscriptionSegmentEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Segments.Add(e.Segment);
            LatestText = e.Segment.Text;
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
    private void TogglePin()
    {
        IsPinned = !IsPinned;
    }

    [RelayCommand]
    private void Close()
    {
        IsOverlayVisible = false;
        // Optionally update settings if user manually closes it, but for now just hide it for this session.
    }
    
    public void Clear()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Segments.Clear();
            LatestText = "";
            StatusText = "Waiting for audio...";
        });
    }
}

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using MeetingRecorder.ViewModels;

namespace MeetingRecorder;

public partial class TranscriptionOverlayWindow : Window
{
    public TranscriptionOverlayWindow()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TranscriptionOverlayViewModel oldVm)
        {
            oldVm.Segments.CollectionChanged -= Segments_CollectionChanged;
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is TranscriptionOverlayViewModel vm)
        {
            vm.Segments.CollectionChanged += Segments_CollectionChanged;
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranscriptionOverlayViewModel.CurrentLiveText) or nameof(TranscriptionOverlayViewModel.HasLiveText))
        {
            TranscriptScrollViewer.ScrollToEnd();
        }
    }

    private void Segments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Auto-scroll to bottom
            TranscriptScrollViewer.ScrollToEnd();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

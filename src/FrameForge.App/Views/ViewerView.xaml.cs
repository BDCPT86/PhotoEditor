using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FrameForge.App.ViewModels;

namespace FrameForge.App.Views;

/// <summary>
/// View-only key routing for the viewer: the suggestions tray takes ←/→/digits/Enter/Esc while open, and holding \
/// shows the original while Develop is open. Everything it calls lives in the view models.
/// </summary>
public partial class ViewerView : UserControl
{
    public ViewerView()
    {
        InitializeComponent();

        // Give the image keyboard focus when the viewer opens, so arrow/zoom keys work immediately.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() => Zoom.Focus(), DispatcherPriority.Loaded);
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        // When Develop or the suggestions tray closes, the control that had focus disappears and keyboard focus would
        // fall out of the page (no shortcut would work until a click). Hand focus back to the photo.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ViewerViewModel old)
            {
                old.Develop.PropertyChanged -= OnDevelopChanged;
            }

            if (e.NewValue is ViewerViewModel current)
            {
                current.Develop.PropertyChanged += OnDevelopChanged;
            }
        };
    }

    private void OnDevelopChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is DevelopViewModel develop
            && ((e.PropertyName == nameof(DevelopViewModel.IsOpen) && !develop.IsOpen)
                || (e.PropertyName == nameof(DevelopViewModel.IsSuggesting) && !develop.IsSuggesting)))
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible && !IsKeyboardFocusWithin)
                {
                    Zoom.Focus();
                }
                else if (IsVisible && Keyboard.FocusedElement is UIElement { IsVisible: false })
                {
                    Zoom.Focus();
                }
            }, DispatcherPriority.Input);
        }
    }

    /// <summary>Gives keyboard focus to the photo (used when a side panel beside the viewer closes).</summary>
    public void FocusPhoto() => Zoom.Focus();

    private DevelopViewModel? Develop => (DataContext as ViewerViewModel)?.Develop;

    private static bool IsCompareKey(Key key) => key is Key.Oem5 or Key.OemBackslash;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Develop is not { IsOpen: true } develop || e.OriginalSource is TextBoxBase || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (develop.HandleSuggestionKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && develop.Masks.IsRefining)
        {
            develop.Masks.IsRefining = false;
            e.Handled = true;
            return;
        }

        if (IsCompareKey(e.Key))
        {
            develop.ShowOriginal = true; // hold to compare
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Develop is { IsOpen: true } develop && IsCompareKey(e.Key) && e.OriginalSource is not TextBoxBase)
        {
            develop.ShowOriginal = false;
            e.Handled = true;
        }
    }

    private void OnSuggestionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SuggestionItem item } && Develop is { } develop)
        {
            develop.AcceptSuggestionCommand.Execute(item);
            e.Handled = true;
        }
    }
}

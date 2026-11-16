using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FrameForge.App.Shortcuts;

public interface ICullingShortcutHandler
{
    /// <returns>True if the action was handled (the key is then marked handled).</returns>
    bool TryHandle(CullingAction action);
}

/// <summary>
/// Attached behaviour: routes unhandled key presses within an element to an <see cref="ICullingShortcutHandler"/>
/// (normally its view model). Uses the bubbling KeyDown, so focused controls (zoom keys, list navigation)
/// keep priority, and ignores keys typed into text input.
/// </summary>
public static class ShortcutBehavior
{
    public static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(ICullingShortcutHandler), typeof(ShortcutBehavior),
        new PropertyMetadata(null, OnHandlerChanged));

    public static ICullingShortcutHandler? GetHandler(DependencyObject element) =>
        (ICullingShortcutHandler?)element.GetValue(HandlerProperty);

    public static void SetHandler(DependencyObject element, ICullingShortcutHandler? value) =>
        element.SetValue(HandlerProperty, value);

    private static void OnHandlerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.KeyDown -= OnKeyDown;
        if (e.NewValue is not null)
        {
            element.KeyDown += OnKeyDown;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.OriginalSource is TextBoxBase || sender is not DependencyObject d || GetHandler(d) is not { } handler)
        {
            return;
        }

        // SystemKey carries the real key when Alt is involved; we never bind Alt, so use Key.
        if (CullingShortcuts.Resolve(e.Key, Keyboard.Modifiers) is { } action && handler.TryHandle(action))
        {
            e.Handled = true;
        }
    }
}

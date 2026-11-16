using System.Windows.Input;
using FrameForge.Core.Decisions;

namespace FrameForge.App.Shortcuts;

public enum CullingAction
{
    Keep,
    Reject,
    Review,
    Unset,
    Rate1,
    Rate2,
    Rate3,
    Rate4,
    Rate5,
    ClearRating,
    LabelRed,
    LabelYellow,
    LabelGreen,
    LabelBlue,
    ClearLabel,
    Undo,
    Redo,
    Compare,
    Refresh,
    ToggleHelp,
}

public sealed record ShortcutHelpItem(string Keys, string Description);

public sealed record ShortcutHelpGroup(string Title, IReadOnlyList<ShortcutHelpItem> Items);

/// <summary>
/// The culling keyboard map: one table drives both key handling and the on-screen help.
/// Plain letter and digit keys are not expressible as WPF <see cref="KeyGesture"/>s, hence this resolver.
/// </summary>
public static class CullingShortcuts
{
    public static CullingAction? Resolve(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control)
        {
            return key switch
            {
                Key.Z => CullingAction.Undo,
                Key.Y => CullingAction.Redo,
                _ => null,
            };
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return key == Key.Z ? CullingAction.Redo : null;
        }

        if (modifiers == ModifierKeys.Shift)
        {
            return key switch
            {
                Key.D0 or Key.NumPad0 => CullingAction.ClearLabel,
                Key.OemQuestion => CullingAction.ToggleHelp, // "?" on US-style layouts
                _ => null,
            };
        }

        if (modifiers != ModifierKeys.None)
        {
            return null;
        }

        return key switch
        {
            Key.P => CullingAction.Keep,
            Key.X => CullingAction.Reject,
            Key.V => CullingAction.Review,
            Key.U => CullingAction.Unset,
            Key.D1 or Key.NumPad1 => CullingAction.Rate1,
            Key.D2 or Key.NumPad2 => CullingAction.Rate2,
            Key.D3 or Key.NumPad3 => CullingAction.Rate3,
            Key.D4 or Key.NumPad4 => CullingAction.Rate4,
            Key.D5 or Key.NumPad5 => CullingAction.Rate5,
            Key.D0 or Key.NumPad0 => CullingAction.ClearRating,
            Key.D6 or Key.NumPad6 => CullingAction.LabelRed,
            Key.D7 or Key.NumPad7 => CullingAction.LabelYellow,
            Key.D8 or Key.NumPad8 => CullingAction.LabelGreen,
            Key.D9 or Key.NumPad9 => CullingAction.LabelBlue,
            Key.C => CullingAction.Compare,
            Key.F5 => CullingAction.Refresh,
            Key.F1 => CullingAction.ToggleHelp,
            _ => null,
        };
    }

    /// <summary>The decision edit an action performs, or null for non-decision actions.</summary>
    public static DecisionEdit? ToEdit(CullingAction action) => action switch
    {
        CullingAction.Keep => DecisionEdit.SetState(CullState.Keep),
        CullingAction.Reject => DecisionEdit.SetState(CullState.Reject),
        CullingAction.Review => DecisionEdit.SetState(CullState.Review),
        CullingAction.Unset => DecisionEdit.SetState(CullState.Unset),
        CullingAction.Rate1 => DecisionEdit.SetRating(1),
        CullingAction.Rate2 => DecisionEdit.SetRating(2),
        CullingAction.Rate3 => DecisionEdit.SetRating(3),
        CullingAction.Rate4 => DecisionEdit.SetRating(4),
        CullingAction.Rate5 => DecisionEdit.SetRating(5),
        CullingAction.ClearRating => DecisionEdit.SetRating(null),
        CullingAction.LabelRed => DecisionEdit.SetLabel(ColourLabel.Red),
        CullingAction.LabelYellow => DecisionEdit.SetLabel(ColourLabel.Yellow),
        CullingAction.LabelGreen => DecisionEdit.SetLabel(ColourLabel.Green),
        CullingAction.LabelBlue => DecisionEdit.SetLabel(ColourLabel.Blue),
        CullingAction.ClearLabel => DecisionEdit.SetLabel(ColourLabel.None),
        _ => null,
    };

    /// <summary>True for Keep/Reject/Review/Unset: the edits that trigger auto-advance in the viewer.</summary>
    public static bool IsStateAction(CullingAction action) =>
        action is CullingAction.Keep or CullingAction.Reject or CullingAction.Review or CullingAction.Unset;

    public static IReadOnlyList<ShortcutHelpGroup> Help { get; } =
    [
        new("Decide (grid: all selected · viewer/compare: current photo)",
        [
            new("P", "Keep"),
            new("X", "Reject"),
            new("V", "Review later"),
            new("U", "Unset decision"),
            new("1 – 5", "Star rating"),
            new("0", "Clear rating"),
            new("6 7 8 9", "Label red / yellow / green / blue"),
            new("Shift+0", "Clear label"),
            new("Ctrl+Z / Ctrl+Y", "Undo / redo"),
        ]),
        new("Navigate",
        [
            new("Arrow keys", "Move in grid (Shift / Ctrl+click to select several)"),
            new("Enter", "Open viewer"),
            new("C", "Compare selected photos (2–4)"),
            new("← / →", "Previous / next photo (viewer) or panel (compare)"),
            new("Esc", "Back to the grid"),
            new("F5", "Re-apply filter and sort"),
            new("Ctrl+I", "Import folder"),
        ]),
        new("Viewer",
        [
            new("Z / double-click", "Fit ↔ 100%"),
            new("+ / −, mouse wheel", "Zoom"),
            new("Drag", "Pan"),
        ]),
        new("Help",
        [
            new("F1 or ?", "Show or hide this panel"),
        ]),
    ];
}

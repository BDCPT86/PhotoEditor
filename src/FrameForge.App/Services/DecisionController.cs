using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.ViewModels;
using FrameForge.Application.Decisions;
using FrameForge.Core.Decisions;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.Services;

/// <summary>Outcome of applying, undoing or redoing a decision change.</summary>
/// <param name="Change">What changed, or null if nothing did.</param>
/// <param name="Error">User-facing message if saving failed (the change was reverted).</param>
public sealed record DecisionOutcome(DecisionChange? Change, string? Error)
{
    public static DecisionOutcome Nothing { get; } = new(null, null);
}

/// <summary>
/// Applies decision edits to tiles, records undo history, and persists changes. The UI updates
/// immediately; if saving fails, the change is reverted and removed from history so the screen,
/// the database and undo always agree. UI thread only.
/// </summary>
public sealed partial class DecisionController(IDecisionService service, ILogger<DecisionController> logger) : ObservableObject
{
    private readonly DecisionHistory _history = new();
    private Dictionary<Guid, PhotoTileViewModel> _tiles = [];
    private Dictionary<Guid, Guid> _coverOf = [];

    /// <summary>Raised after tiles change locally (apply, undo, redo, or revert).</summary>
    public event EventHandler<DecisionChange>? DecisionsChanged;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>
    /// Replaces the set of tiles that changes are applied to (after a library reload). The hidden RAW file of each pair
    /// (<see cref="PhotoTileViewModel.Partner"/>) is registered with its cover, and every change to the cover reaches it.
    /// </summary>
    public void Register(IEnumerable<PhotoTileViewModel> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = [];
        _coverOf = [];
        foreach (var tile in tiles)
        {
            _tiles[tile.Id] = tile;
            if (tile.Partner is { } partner)
            {
                _tiles[partner.Id] = partner;
                _coverOf[partner.Id] = tile.Id;
            }
        }
    }

    /// <summary>How many photos a change touched, counting a RAW + JPEG pair once.</summary>
    public int PhotoCount(DecisionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Deltas.Select(d => _coverOf.GetValueOrDefault(d.PhotoId, d.PhotoId)).Distinct().Count();
    }

    public async Task<DecisionOutcome> ApplyAsync(IReadOnlyList<PhotoTileViewModel> targets, DecisionEdit edit)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(edit);

        var partners = targets
            .Where(t => t.Partner is not null)
            .Select(t => new PartnerDecision(t.Id, t.Partner!.Id, t.Partner.Decision))
            .ToList();
        var change = service.Plan(targets.Select(t => (t.Id, t.Decision)).ToList(), edit, partners);
        if (change is null)
        {
            return DecisionOutcome.Nothing;
        }

        SetLocal(change, change.AfterValues);
        _history.Record(change);
        NotifyHistory();

        if (await TrySaveAsync(change.AfterValues).ConfigureAwait(true))
        {
            return new DecisionOutcome(change, null);
        }

        SetLocal(change, change.BeforeValues);
        _history.Discard(change);
        NotifyHistory();
        return new DecisionOutcome(change, "The decision could not be saved and was reverted. See the log for details.");
    }

    public async Task<DecisionOutcome> UndoAsync()
    {
        if (_history.Undo() is not { } change)
        {
            return DecisionOutcome.Nothing;
        }

        SetLocal(change, change.BeforeValues);
        NotifyHistory();
        if (await TrySaveAsync(change.BeforeValues).ConfigureAwait(true))
        {
            return new DecisionOutcome(change, null);
        }

        // Put screen and history back as they were before the undo.
        _history.Redo();
        SetLocal(change, change.AfterValues);
        NotifyHistory();
        return new DecisionOutcome(change, "Undo could not be saved. See the log for details.");
    }

    public async Task<DecisionOutcome> RedoAsync()
    {
        if (_history.Redo() is not { } change)
        {
            return DecisionOutcome.Nothing;
        }

        SetLocal(change, change.AfterValues);
        NotifyHistory();
        if (await TrySaveAsync(change.AfterValues).ConfigureAwait(true))
        {
            return new DecisionOutcome(change, null);
        }

        _history.Undo();
        SetLocal(change, change.BeforeValues);
        NotifyHistory();
        return new DecisionOutcome(change, "Redo could not be saved. See the log for details.");
    }

    private async Task<bool> TrySaveAsync(IReadOnlyList<(Guid PhotoId, PhotoDecision Decision)> values)
    {
        try
        {
            await service.SaveAsync(values).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex, values.Count);
            return false;
        }
    }

    private void SetLocal(DecisionChange change, IReadOnlyList<(Guid PhotoId, PhotoDecision Decision)> values)
    {
        foreach (var (id, decision) in values)
        {
            if (_tiles.TryGetValue(id, out var tile))
            {
                tile.Decision = decision;
            }
        }

        DecisionsChanged?.Invoke(this, change);
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving {Count} decision(s) failed; the change was reverted.")]
    private partial void LogSaveFailed(Exception exception, int count);
}

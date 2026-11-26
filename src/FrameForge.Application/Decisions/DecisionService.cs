using FrameForge.Core.Decisions;

namespace FrameForge.Application.Decisions;

public interface IPhotoDecisionRepository
{
    /// <summary>All stored decisions. Photos without an entry are <see cref="PhotoDecision.Unset"/>.</summary>
    Task<IReadOnlyDictionary<Guid, PhotoDecision>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores decisions; empty decisions remove the stored entry.</summary>
    Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> decisions, CancellationToken cancellationToken = default);
}

public interface IDecisionService
{
    Task<IReadOnlyDictionary<Guid, PhotoDecision>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Computes the change an edit would make; photos already in the target state are omitted.</summary>
    /// <param name="partners">
    /// The hidden RAW files of paired targets (M09). Each gets the decision its cover ends up with, even when the cover
    /// itself is already in the target state, so a pair whose files disagreed is brought back in step.
    /// </param>
    /// <returns>Null when the edit changes nothing.</returns>
    DecisionChange? Plan(IReadOnlyList<(Guid PhotoId, PhotoDecision Current)> targets, DecisionEdit edit, IReadOnlyList<PartnerDecision>? partners = null);

    /// <summary>Persists values. Saves are serialised so a later change can never be overwritten by an earlier one.</summary>
    Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> values, CancellationToken cancellationToken = default);
}

public sealed class DecisionService(IPhotoDecisionRepository repository) : IDecisionService, IDisposable
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <remarks>Waits for any in-flight save, so a reload never reads a state older than what is on screen.</remarks>
    public async Task<IReadOnlyDictionary<Guid, PhotoDecision>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public DecisionChange? Plan(IReadOnlyList<(Guid PhotoId, PhotoDecision Current)> targets, DecisionEdit edit, IReadOnlyList<PartnerDecision>? partners = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(edit);

        var deltas = new List<DecisionDelta>(targets.Count);
        var seen = new HashSet<Guid>();
        foreach (var (id, current) in targets)
        {
            if (!seen.Add(id))
            {
                continue;
            }

            var after = edit.ApplyTo(current);
            if (after != current)
            {
                deltas.Add(new DecisionDelta(id, current, after));
            }
        }

        if (partners is { Count: > 0 })
        {
            var covers = new Dictionary<Guid, PhotoDecision>(targets.Count);
            foreach (var (id, current) in targets)
            {
                covers.TryAdd(id, current);
            }

            foreach (var partner in partners)
            {
                if (covers.TryGetValue(partner.CoverId, out var cover) && seen.Add(partner.PartnerId))
                {
                    var after = edit.ApplyTo(cover);
                    if (after != partner.Current)
                    {
                        deltas.Add(new DecisionDelta(partner.PartnerId, partner.Current, after));
                    }
                }
            }
        }

        return deltas.Count == 0 ? null : new DecisionChange(edit.Description, deltas);
    }

    public async Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return;
        }

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.SaveAsync(values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose() => _saveGate.Dispose();
}

/// <summary>The hidden RAW file of a RAW + JPEG pair, with its own stored decision (see <see cref="Core.Photos.PhotoPairing"/>).</summary>
public sealed record PartnerDecision(Guid CoverId, Guid PartnerId, PhotoDecision Current);

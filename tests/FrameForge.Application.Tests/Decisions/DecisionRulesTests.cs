using FrameForge.Application.Decisions;
using FrameForge.Core.Decisions;

namespace FrameForge.Application.Tests.Decisions;

public sealed class DecisionRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_OutsideOneToFive_IsRejected(int rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoDecision(CullState.Keep, rating));

    [Fact]
    public void UndefinedEnums_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoDecision((CullState)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoDecision(label: (ColourLabel)99));
    }

    [Fact]
    public void Unset_IsEmpty_AndAnyFieldMakesItNonEmpty()
    {
        Assert.True(PhotoDecision.Unset.IsEmpty);
        Assert.False(PhotoDecision.Unset.WithState(CullState.Review).IsEmpty);
        Assert.False(PhotoDecision.Unset.WithRating(1).IsEmpty);
        Assert.False(PhotoDecision.Unset.WithLabel(ColourLabel.Blue).IsEmpty);
    }

    [Fact]
    public void Edits_ChangeOnlyTheirOwnField()
    {
        var start = new PhotoDecision(CullState.Keep, 3, ColourLabel.Red);

        Assert.Equal(new PhotoDecision(CullState.Reject, 3, ColourLabel.Red), DecisionEdit.SetState(CullState.Reject).ApplyTo(start));
        Assert.Equal(new PhotoDecision(CullState.Keep, 5, ColourLabel.Red), DecisionEdit.SetRating(5).ApplyTo(start));
        Assert.Equal(new PhotoDecision(CullState.Keep, null, ColourLabel.Red), DecisionEdit.SetRating(null).ApplyTo(start));
        Assert.Equal(new PhotoDecision(CullState.Keep, 3, ColourLabel.None), DecisionEdit.SetLabel(ColourLabel.None).ApplyTo(start));
    }

    [Fact]
    public void InvalidEdit_FailsAtCreation() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionEdit.SetRating(9));

    [Fact]
    public void Plan_OmitsPhotosAlreadyInTargetState_AndDuplicates()
    {
        using var service = new DecisionService(new InMemoryDecisionRepository());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var keep = PhotoDecision.Unset.WithState(CullState.Keep);

        var change = service.Plan([(a, PhotoDecision.Unset), (b, keep), (a, PhotoDecision.Unset)], DecisionEdit.SetState(CullState.Keep));

        var delta = Assert.Single(change!.Deltas);
        Assert.Equal(a, delta.PhotoId);
        Assert.Equal(PhotoDecision.Unset, delta.Before);
        Assert.Equal(keep, delta.After);
        Assert.Equal("Marked Keep", change.Description);
    }

    [Fact]
    public void Plan_ReturnsNull_WhenNothingChanges()
    {
        using var service = new DecisionService(new InMemoryDecisionRepository());
        Assert.Null(service.Plan([(Guid.NewGuid(), PhotoDecision.Unset)], DecisionEdit.SetState(CullState.Unset)));
    }

    [Fact]
    public void Plan_GivesAPartnerTheDecisionItsCoverEndsUpWith()
    {
        using var service = new DecisionService(new InMemoryDecisionRepository());
        var cover = Guid.NewGuid();
        var raw = Guid.NewGuid();

        var change = service.Plan([(cover, new PhotoDecision(CullState.Keep))], DecisionEdit.SetRating(4), [new PartnerDecision(cover, raw, PhotoDecision.Unset)]);

        Assert.Equal(2, change!.Deltas.Count);
        var partner = change.Deltas.Single(d => d.PhotoId == raw);
        Assert.Equal(PhotoDecision.Unset, partner.Before);
        Assert.Equal(new PhotoDecision(CullState.Keep, 4), partner.After);
    }

    [Fact]
    public void Plan_BringsADisagreeingPartnerIntoStep_EvenWhenTheCoverIsUnchanged()
    {
        using var service = new DecisionService(new InMemoryDecisionRepository());
        var cover = Guid.NewGuid();
        var raw = Guid.NewGuid();
        var keep = new PhotoDecision(CullState.Keep, 3);

        var change = service.Plan([(cover, keep)], DecisionEdit.SetState(CullState.Keep), [new PartnerDecision(cover, raw, new PhotoDecision(CullState.Reject))]);

        var delta = Assert.Single(change!.Deltas);
        Assert.Equal(raw, delta.PhotoId);
        Assert.Equal(new PhotoDecision(CullState.Reject), delta.Before);
        Assert.Equal(keep, delta.After);
    }

    [Fact]
    public void Plan_IgnoresPartnersOfPhotosNotTargeted_AndPartnersAlreadyInStep()
    {
        using var service = new DecisionService(new InMemoryDecisionRepository());
        var cover = Guid.NewGuid();
        var keep = new PhotoDecision(CullState.Keep);

        Assert.Null(service.Plan(
            [(cover, keep)],
            DecisionEdit.SetState(CullState.Keep),
            [new PartnerDecision(cover, Guid.NewGuid(), keep), new PartnerDecision(Guid.NewGuid(), Guid.NewGuid(), PhotoDecision.Unset)]));
    }

    [Fact]
    public void History_ForgetsChangesToRemovedPhotos_AndKeepsTheRestInOrder()
    {
        var history = new DecisionHistory();
        var (gone, kept) = (Guid.NewGuid(), Guid.NewGuid());
        DecisionChange Change(string name, Guid id) => new(name, [new DecisionDelta(id, PhotoDecision.Unset, new PhotoDecision(CullState.Keep))]);
        history.Record(Change("a", kept));
        history.Record(Change("b", gone));
        history.Record(Change("c", kept));
        history.Record(Change("d", gone));
        history.Undo(); // d → redo
        history.Undo(); // c → redo

        history.Forget(new HashSet<Guid> { gone });

        Assert.Equal("c", history.Redo()!.Description); // d was forgotten
        Assert.Null(history.Redo());
        Assert.Equal("c", history.Undo()!.Description);
        Assert.Equal("a", history.Undo()!.Description); // b was forgotten
        Assert.Null(history.Undo());
    }

    [Fact]
    public async Task Saves_AreSerialised_AndLoadWaitsForThem()
    {
        var repository = new InMemoryDecisionRepository { SaveDelay = TimeSpan.FromMilliseconds(30) };
        using var service = new DecisionService(repository);
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var first = service.SaveAsync([(id, PhotoDecision.Unset.WithState(CullState.Keep))], ct);
        var second = service.SaveAsync([(id, PhotoDecision.Unset.WithState(CullState.Reject))], ct);
        var loaded = await service.LoadAsync(ct);
        await Task.WhenAll(first, second);

        Assert.Equal(1, repository.MaxConcurrentSaves);
        Assert.Equal(CullState.Reject, repository.Stored[id].State); // later save wins
        Assert.Equal(CullState.Reject, loaded[id].State);          // load saw both saves
    }
}

internal sealed class InMemoryDecisionRepository : IPhotoDecisionRepository
{
    private int _active;

    public Dictionary<Guid, PhotoDecision> Stored { get; } = [];

    public TimeSpan SaveDelay { get; init; }

    public int MaxConcurrentSaves { get; private set; }

    public Task<IReadOnlyDictionary<Guid, PhotoDecision>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PhotoDecision>>(new Dictionary<Guid, PhotoDecision>(Stored));

    public async Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> decisions, CancellationToken cancellationToken = default)
    {
        var active = Interlocked.Increment(ref _active);
        MaxConcurrentSaves = Math.Max(MaxConcurrentSaves, active);
        try
        {
            await Task.Delay(SaveDelay, cancellationToken);
            foreach (var (id, decision) in decisions)
            {
                if (decision.IsEmpty)
                {
                    Stored.Remove(id);
                }
                else
                {
                    Stored[id] = decision;
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

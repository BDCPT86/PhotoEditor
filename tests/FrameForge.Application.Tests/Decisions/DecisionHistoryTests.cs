using FrameForge.Application.Decisions;
using FrameForge.Core.Decisions;

namespace FrameForge.Application.Tests.Decisions;

public sealed class DecisionHistoryTests
{
    private static DecisionChange Change(string name) =>
        new(name, [new DecisionDelta(Guid.NewGuid(), PhotoDecision.Unset, PhotoDecision.Unset.WithState(CullState.Keep))]);

    [Fact]
    public void UndoAndRedo_ReturnChangesInReverseOrder()
    {
        var history = new DecisionHistory();
        var a = Change("a");
        var b = Change("b");
        history.Record(a);
        history.Record(b);

        Assert.Same(b, history.Undo());
        Assert.Same(a, history.Undo());
        Assert.Null(history.Undo());
        Assert.Same(a, history.Redo());
        Assert.Same(b, history.Redo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void RecordingAfterUndo_ClearsRedo()
    {
        var history = new DecisionHistory();
        history.Record(Change("a"));
        history.Undo();

        history.Record(Change("b"));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Capacity_DropsOldestChanges()
    {
        var history = new DecisionHistory(capacity: 3);
        var changes = Enumerable.Range(0, 5).Select(i => Change(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        changes.ForEach(history.Record);

        Assert.Equal(3, history.UndoCount);
        Assert.Same(changes[4], history.Undo());
        Assert.Same(changes[3], history.Undo());
        Assert.Same(changes[2], history.Undo());
        Assert.Null(history.Undo());
    }

    [Fact]
    public void Discard_RemovesAChangeThatFailedToSave()
    {
        var history = new DecisionHistory();
        var kept = Change("kept");
        var failed = Change("failed");
        history.Record(kept);
        history.Record(failed);

        history.Discard(failed);

        Assert.Same(kept, history.Undo());
        Assert.False(history.CanUndo);
    }
}

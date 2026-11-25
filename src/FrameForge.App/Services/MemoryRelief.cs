namespace FrameForge.App.Services;

/// <summary>
/// Gives memory back after work that went through many large image buffers (closing the viewer, an edited export).
/// The buffers are garbage by then, but large objects are only reclaimed by a full collection, and an idle app gives
/// the garbage collector no reason to run one, so the memory stays with the process (measured: about 200 MB after a
/// 20-photo export; see docs/benchmarks/m07-performance.md). One aggressive collection, on a worker thread, returns it.
/// </summary>
public static class MemoryRelief
{
    public static void AfterLargeWork() =>
        _ = Task.Run(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true));
}

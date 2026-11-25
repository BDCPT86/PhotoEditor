using System.Threading;

namespace FrameForge.Evaluation;

/// <summary>Samples the process working set every 10 ms, so a native peak between two calls is seen.</summary>
internal sealed class MemorySampler : IDisposable
{
    private readonly Timer _timer;
    private long _peak;

    public MemorySampler() => _timer = new Timer(_ => Sample(), null, 0, 10);

    public double PeakMb
    {
        get
        {
            Sample();
            return Interlocked.Read(ref _peak) / (1024d * 1024);
        }
    }

    public void Reset() => Interlocked.Exchange(ref _peak, Environment.WorkingSet);

    public void Dispose() => _timer.Dispose();

    private void Sample()
    {
        var now = Environment.WorkingSet;
        long seen;
        while (now > (seen = Interlocked.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen)
        {
        }
    }
}

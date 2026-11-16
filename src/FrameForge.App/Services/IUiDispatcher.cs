using System.Windows.Threading;

namespace FrameForge.App.Services;

/// <summary>Posts work to the UI thread. Abstracted so view models and services are testable.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

internal sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Background);
}

using Microsoft.ML.OnnxRuntime;

namespace FrameForge.AI;

/// <summary>Runs a session so that cancelling the token stops a run that is already in progress.</summary>
internal static class OnnxRun
{
    public static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(InferenceSession session, IReadOnlyCollection<NamedOnnxValue> inputs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var options = new RunOptions();
        using var registration = cancellationToken.Register(() => options.Terminate = true);
        try
        {
            return session.Run(inputs, session.OutputNames, options);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

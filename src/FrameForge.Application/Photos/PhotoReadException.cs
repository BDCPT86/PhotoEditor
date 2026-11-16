namespace FrameForge.Application.Photos;

/// <summary>An image file exists but cannot be decoded (corrupt, truncated, or unsupported).</summary>
public sealed class PhotoReadException : Exception
{
    public PhotoReadException()
    {
    }

    public PhotoReadException(string message)
        : base(message)
    {
    }

    public PhotoReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

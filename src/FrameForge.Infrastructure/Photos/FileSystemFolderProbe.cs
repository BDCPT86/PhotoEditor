using FrameForge.Application.Photos;

namespace FrameForge.Infrastructure.Photos;

/// <summary>Checks folders with <see cref="Directory.Exists(string)"/>, which never throws (false on any error).</summary>
public sealed class FileSystemFolderProbe : IFolderProbe
{
    public bool Exists(string folderPath) => Directory.Exists(folderPath);
}

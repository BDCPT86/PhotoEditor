namespace FrameForge.Core.Photos;

/// <summary>Display name of the camera from EXIF make and model.</summary>
public static class CameraName
{
    /// <summary>
    /// Joins make and model, but drops the make when the model already starts with it (Canon writes make "Canon" and
    /// model "Canon EOS R50", which would otherwise read "Canon Canon EOS R50"). Returns "" when both are missing.
    /// </summary>
    public static string Format(string? make, string? model)
    {
        make = make?.Trim();
        model = model?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            return make ?? string.Empty;
        }

        if (string.IsNullOrEmpty(make) || model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return $"{make} {model}";
    }
}

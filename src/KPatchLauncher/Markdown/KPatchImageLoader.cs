using Avalonia.Media;
using Avalonia.Media.Imaging;
using KPatchCore.Common;

namespace KPatchLauncher.Markdown;

/// <summary>
/// Loads images from .kpatch archive entries for markdown rendering.
/// </summary>
public static class KPatchImageLoader
{
    public static IImage? TryLoad(string url, Func<string, Stream?>? openAsset)
    {
        if (!PatchArchivePaths.TryNormalize(url, out _))
            return null;

        var stream = openAsset?.Invoke(url);
        if (stream == null)
            return null;

        try
        {
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
        finally
        {
            stream.Dispose();
        }
    }
}

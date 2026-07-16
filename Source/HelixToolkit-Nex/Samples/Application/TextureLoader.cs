using HelixToolkit.Nex;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;

namespace Demo.Utils;

public static class TextureUtils
{
    private static readonly ILogger _logger = LogManager.Create("TextureLoader");

    public static TextureRef TryLoadIcon(ITextureRepository repo, string path, string debugName)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("Icon not found: {Path}", path);
            return TextureRef.Null;
        }

        try
        {
            return repo.GetOrCreateFromFile(path, debugName: debugName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load icon: {Path}", path);
            return TextureRef.Null;
        }
    }

    public static TextureRef TryLoadIconFromAssets(
        ITextureRepository repo,
        string iconName,
        string debugName
    )
    {
        string iconDir = Path.Join(Paths.AssetsDir, "Icons");
        return TryLoadIcon(repo, Path.Join(iconDir, iconName), debugName);
    }
}

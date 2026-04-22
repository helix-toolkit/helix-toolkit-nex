namespace HelixToolkit.Nex.Sample.Application;

public static class Paths
{
    public static readonly string AssetsDir = FindAssetsDirectory();

    public static readonly string AssetsTextureDir = Path.Combine(AssetsDir, "Textures");

    private static string FindAssetsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var assets = Path.Combine(dir.FullName, "Assets");
            if (Directory.Exists(assets))
                return assets;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Assets directory.");
    }
}

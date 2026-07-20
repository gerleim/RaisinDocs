using System.IO;

namespace RaisinDocs;

internal static class ProjectRootFinder
{
    public const string MarkerDirectoryName = ".raisindocs";

    public static string? FindProjectRoot(string basePath)
    {
        var dir = basePath;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, MarkerDirectoryName)))
                return dir;
            if (File.Exists(Path.Combine(dir, SpellCheckService.ProjectDictionaryFileName)))
                return dir;
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static string SetProjectFolder(string folder)
    {
        var markerDir = Path.Combine(folder, MarkerDirectoryName);
        if (!Directory.Exists(markerDir))
            Directory.CreateDirectory(markerDir);
        return folder;
    }
}

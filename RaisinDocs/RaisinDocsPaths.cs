using System.IO;

namespace RaisinDocs;

internal static class RaisinDocsPaths
{
    public const string MarkerDirectoryName = ".raisindocs";
    public const string ProjectDictionaryFileName = "custom-dictionary.txt";

    public static string? FindProjectRoot(string basePath)
    {
        var dir = basePath;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, MarkerDirectoryName)))
                return dir;
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static string GetProjectDictionaryPath(string projectFolder)
    {
        return Path.Combine(projectFolder, MarkerDirectoryName, ProjectDictionaryFileName);
    }

    public static string? GetUserDictionaryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) return null;
        return Path.Combine(appData, "Raisin", "RaisinDocs", "user-dictionary.txt");
    }

    public static string SetProjectFolder(string folder)
    {
        var markerDir = Path.Combine(folder, MarkerDirectoryName);
        if (!Directory.Exists(markerDir))
            Directory.CreateDirectory(markerDir);
        MergeNestedMarkers(folder, markerDir);
        return folder;
    }

    private static void MergeNestedMarkers(string folder, string targetMarkerDir)
    {
        try
        {
            foreach (var nestedDir in Directory.EnumerateDirectories(folder, MarkerDirectoryName, SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(nestedDir), Path.GetFullPath(targetMarkerDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var file in Directory.EnumerateFiles(nestedDir))
                {
                    var targetFile = Path.Combine(targetMarkerDir, Path.GetFileName(file));
                    if (File.Exists(targetFile))
                        MergeFiles(targetFile, file);
                    else
                        File.Move(file, targetFile);
                }
                Directory.Delete(nestedDir, true);
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static void MergeFiles(string targetPath, string sourcePath)
    {
        var existing = new HashSet<string>(File.ReadLines(targetPath), StringComparer.OrdinalIgnoreCase);
        using var writer = File.AppendText(targetPath);
        foreach (var line in File.ReadLines(sourcePath))
        {
            if (existing.Add(line))
                writer.WriteLine(line);
        }
    }
}

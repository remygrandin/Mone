using System.Diagnostics;

namespace Mone.PluginEngine.Tests;

internal static class TestPluginBuilder
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    public static string BuildPlugin(string pluginProjectName)
    {
        var projectDir = Path.Combine(SolutionRoot, "tests", "TestPlugins", pluginProjectName);
        var outputDir = Path.Combine(projectDir, "bin", "TestOutput");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectDir}\" -o \"{outputDir}\" -c Release --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit(60_000);

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to build {pluginProjectName}: {stderr}");
        }

        var dllPath = Path.Combine(outputDir, $"{pluginProjectName}.dll");
        if (!File.Exists(dllPath))
        {
            var candidates = Directory.GetFiles(outputDir, "*.dll")
                .Where(f => !Path.GetFileName(f).StartsWith("Mone.", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 1)
                return candidates[0];
            throw new FileNotFoundException($"Plugin DLL not found at {dllPath}");
        }

        return dllPath;
    }

    public static string CopyPluginTo(string sourceDllPath, string targetDirectory, string? renameMainDllTo = null)
    {
        Directory.CreateDirectory(targetDirectory);
        var sourceDir = Path.GetDirectoryName(sourceDllPath)!;
        var sourceDllName = Path.GetFileName(sourceDllPath);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            if (renameMainDllTo is not null && fileName == sourceDllName)
                fileName = renameMainDllTo;
            var destFile = Path.Combine(targetDirectory, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        var resultDll = renameMainDllTo ?? sourceDllName;
        return Path.Combine(targetDirectory, resultDll);
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mone.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find Mone.slnx in any parent directory");
    }
}

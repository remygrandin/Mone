using System.Diagnostics;

namespace Mone.Infrastructure.Tests;

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

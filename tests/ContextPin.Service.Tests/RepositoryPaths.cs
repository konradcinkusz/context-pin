namespace ContextPin.Service.Tests;

/// <summary>
/// Locates the checked-out repository root by walking up from the test
/// assembly's output directory until Directory.Build.props is found.
/// </summary>
/// <remarks>
/// Used by tests that need to read source files directly (migration .sql
/// files) rather than relying on CopyToOutputDirectory semantics propagating
/// from a referenced project into this one's output — reading the source
/// directly removes that uncertainty rather than depending on it.
/// </remarks>
public static class RepositoryPaths
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate the repository root (no Directory.Build.props found " +
                $"above {AppContext.BaseDirectory}).");
    }
}

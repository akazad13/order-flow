namespace OrderFlow.ContractTests;

internal static class RepoPath
{
    /// <summary>Resolves a path relative to the repo root (the folder holding OrderFlow.slnx).</summary>
    public static string Combine(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderFlow.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not find the repo root (OrderFlow.slnx).");
        }

        return Path.Combine([dir.FullName, .. parts]);
    }
}

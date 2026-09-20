namespace TalentBridge.Search.Api.Configuration;

/// <summary>
/// Loads the repo-root <c>.env</c> into the process environment for local
/// <c>dotnet run</c>, so the shared secret never lives in appsettings. Existing
/// environment variables win. Under docker-compose the file is injected via
/// <c>env_file</c> and this finds nothing to do.
/// </summary>
public static class DotEnv
{
    public static void Load(string startDirectory)
    {
        var path = FindUpwards(startDirectory, ".env");
        if (path is null)
        {
            return;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindUpwards(string start, string fileName)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

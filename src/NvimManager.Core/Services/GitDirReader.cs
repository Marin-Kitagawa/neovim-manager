using System.Text.RegularExpressions;

namespace NvimManager.Core.Services;

/// <summary>
/// Reads git metadata straight from a plugin's .git directory (HEAD, remotes,
/// tags) without spawning git processes, so scanning hundreds of plugins stays fast.
/// Handles loose refs, packed-refs, detached HEADs and .git link files (worktrees).
/// </summary>
public static partial class GitDirReader
{
    /// <summary>Resolves the real .git directory for a plugin checkout, following gitlink files.</summary>
    public static string? TryResolveGitDir(string pluginDir)
    {
        try
        {
            string dotGit = Path.Combine(pluginDir, ".git");
            if (Directory.Exists(dotGit)) return dotGit;
            if (File.Exists(dotGit))
            {
                string? line = File.ReadLines(dotGit).FirstOrDefault()?.Trim();
                if (line is not null && line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    string target = line["gitdir:".Length..].Trim();
                    string resolved = Path.IsPathRooted(target)
                        ? target
                        : Path.GetFullPath(Path.Combine(pluginDir, target));
                    if (Directory.Exists(resolved)) return resolved;
                }
            }
        }
        catch
        {
            // unreadable checkout -> treated as non-git
        }
        return null;
    }

    public static bool HasGit(string pluginDir) => TryResolveGitDir(pluginDir) is not null;

    /// <summary>Resolves HEAD to a full commit SHA without running git.</summary>
    public static bool TryGetHeadSha(string pluginDir, out string? sha)
    {
        sha = null;
        try
        {
            string? gitDir = TryResolveGitDir(pluginDir);
            if (gitDir is null) return false;
            string headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath)) return false;
            string head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                string refName = head["ref:".Length..].Trim();
                return TryResolveRef(gitDir, refName, out sha);
            }
            if (IsSha(head))
            {
                sha = head;
                return true;
            }
        }
        catch
        {
            // fall through
        }
        return false;
    }

    /// <summary>Resolves any ref (branch, remote-tracking, tag) to a SHA. Follows symrefs.</summary>
    public static bool TryResolveRef(string gitDir, string refName, out string? sha)
    {
        sha = null;
        try
        {
            string current = refName;
            for (int i = 0; i < 5; i++)
            {
                string loose = Path.Combine(gitDir, current.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose))
                {
                    string content = File.ReadAllText(loose).Trim();
                    if (content.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                    {
                        current = content["ref:".Length..].Trim();
                        continue;
                    }
                    if (IsSha(content))
                    {
                        sha = content;
                        return true;
                    }
                    return false;
                }
                if (TryFindPackedRef(gitDir, current, out sha)) return true;
                return false;
            }
        }
        catch
        {
            // fall through
        }
        return false;
    }

    private static bool TryFindPackedRef(string gitDir, string refName, out string? sha)
    {
        sha = null;
        try
        {
            string packed = Path.Combine(gitDir, "packed-refs");
            if (!File.Exists(packed)) return false;
            foreach (string raw in File.ReadLines(packed))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or '^') continue;
                int space = line.IndexOf(' ');
                if (space <= 0) continue;
                if (string.Equals(line[(space + 1)..].Trim(), refName, StringComparison.Ordinal)
                    && IsSha(line[..space]))
                {
                    sha = line[..space];
                    return true;
                }
            }
        }
        catch
        {
            // fall through
        }
        return false;
    }

    /// <summary>Returns the tag name if HEAD is exactly tagged, else null.</summary>
    public static string? TryGetExactTag(string pluginDir, string headSha)
    {
        try
        {
            string? gitDir = TryResolveGitDir(pluginDir);
            if (gitDir is null) return null;
            string tagsDir = Path.Combine(gitDir, "refs", "tags");
            if (Directory.Exists(tagsDir))
            {
                foreach (string file in Directory.EnumerateFiles(tagsDir, "*", SearchOption.AllDirectories))
                {
                    string content = File.ReadAllText(file).Trim();
                    if (content.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(content, headSha, StringComparison.OrdinalIgnoreCase))
                        return Path.GetRelativePath(tagsDir, file).Replace(Path.DirectorySeparatorChar, '/');
                }
            }
            string packed = Path.Combine(gitDir, "packed-refs");
            if (File.Exists(packed))
            {
                foreach (string raw in File.ReadLines(packed))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] is '#' or '^') continue;
                    int space = line.IndexOf(' ');
                    if (space <= 0) continue;
                    string name = line[(space + 1)..].Trim();
                    if (name.StartsWith("refs/tags/", StringComparison.Ordinal)
                        && string.Equals(line[..space], headSha, StringComparison.OrdinalIgnoreCase))
                        return name["refs/tags/".Length..];
                }
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    /// <summary>Reads the URL of the given remote (default "origin") from .git/config.</summary>
    public static string? TryGetRemoteUrl(string pluginDir, string remote = "origin")
    {
        try
        {
            string? gitDir = TryResolveGitDir(pluginDir);
            if (gitDir is null) return null;
            string config = Path.Combine(gitDir, "config");
            if (!File.Exists(config)) return null;
            string? section = null;
            foreach (string raw in File.ReadLines(config))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                if (line[0] == '[')
                {
                    section = ParseSection(line);
                    continue;
                }
                if (section is not null
                    && section.StartsWith("remote ", StringComparison.OrdinalIgnoreCase)
                    && section["remote ".Length..].Trim().Trim('"')
                        .Equals(remote, StringComparison.Ordinal))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0
                        && line[..eq].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
                        return line[(eq + 1)..].Trim();
                }
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    private static string? ParseSection(string line)
    {
        // [remote "origin"]  (also tolerates [remote origin])
        var m = RemoteSectionRegex().Match(line);
        if (m.Success) return "remote " + m.Groups["name"].Value;
        var core = CoreSectionRegex().Match(line);
        if (core.Success) return core.Groups["name"].Value.ToLowerInvariant();
        return null;
    }

    /// <summary>
    /// Normalizes a git remote URL to (host, owner/path). Returns false for
    /// local paths. Handles https://, ssh://, git:// and scp-like git@host:path forms.
    /// </summary>
    public static bool TryNormalizeRepoUrl(string? url, out string host, out string ownerPath)
    {
        host = string.Empty;
        ownerPath = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        string u = url.Trim();
        if (u.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            u = u[..^".git".Length];

        // scp-like syntax: [user@]host:path (a single-letter "host" is a Windows drive, not a remote)
        var scp = ScpRegex().Match(u);
        if (scp.Success && !u.Contains("://"))
        {
            host = scp.Groups["host"].Value.ToLowerInvariant();
            if (host.Length == 1) return false;
            ownerPath = scp.Groups["path"].Value.Trim('/').TrimEnd('/');
            return ownerPath.Length > 0;
        }

        if (Uri.TryCreate(u, UriKind.Absolute, out var uri)
            && (uri.Scheme is "https" or "http" or "ssh" or "git")
            && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host.ToLowerInvariant();
            ownerPath = uri.AbsolutePath.Trim('/').TrimEnd('/');
            return ownerPath.Length > 0;
        }

        return false;
    }

    private static bool IsSha(string? value)
        => value is not null && value.Length == 40 && value.All(IsHex);

    private static bool IsHex(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    [GeneratedRegex("""^\[\s*remote\s+"?(?<name>[^"\]]+)"?\s*\]""", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteSectionRegex();

    [GeneratedRegex("""^\[\s*(?<name>[^\s\]]+)\s*\]""")]
    private static partial Regex CoreSectionRegex();

    [GeneratedRegex("""^(?:[^@:/]+@)?(?<host>[^:/]+):(?<path>.+)$""")]
    private static partial Regex ScpRegex();
}
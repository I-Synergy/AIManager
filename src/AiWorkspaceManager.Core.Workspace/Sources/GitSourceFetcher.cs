using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Resolves remote sources with <c>git</c>: a shallow clone at the pinned ref, a fetch by commit
/// SHA when the ref is not a branch, and read-only <c>ls-remote</c> for release tags. The cache is
/// keyed by repository and ref, so two source ids on one repository share a checkout.
/// </summary>
public sealed partial class GitSourceFetcher : ISourceFetcher
{
    private const int TagTimeoutMs = 15_000;

    private readonly WorkspacePaths paths;
    private readonly bool offline;

    public GitSourceFetcher(WorkspacePaths paths, bool offline)
    {
        this.paths = paths;
        this.offline = offline;
    }

    public string Fetch(string name, string repo, string reference, bool refresh)
    {
        var url = Url(repo);
        if (offline && !url.StartsWith("file://", StringComparison.Ordinal))
        {
            throw new ResolverException(
                $"CATALOG_OFFLINE is set, so source '{name}' ({repo} @ {reference}) was not "
                + "fetched — unset it, or give that source a local \"path\"");
        }

        var target = CacheDirectory(repo, reference);
        var hasCache = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
        if (hasCache && !refresh)
        {
            return target;
        }

        if (hasCache)
        {
            // Refresh in place: a shallow fetch downloads only the changed objects. Deleting and
            // cloning again made every "check for updates" pay the full repository download.
            Console.Error.WriteLine($"refreshing {name} @ {reference} ...");
            var started = Stopwatch.GetTimestamp();
            var fetch = Run(
                "git",
                ["-C", target, "fetch", "--quiet", "--depth", "1", "origin", reference],
                timeoutMs: 60_000);
            if (fetch.Code == 0)
            {
                var checkout = Run(
                    "git",
                    ["-C", target, "checkout", "--quiet", "--force", "FETCH_HEAD"],
                    timeoutMs: 30_000);
                if (checkout.Code == 0)
                {
                    Console.Error.WriteLine(
                        $"refreshed {name} @ {reference} in "
                        + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
                    return target;
                }
            }

            Console.Error.WriteLine($"refresh of {name} failed; cloning again ...");
            ForceRemoveTree(target);
        }

        Console.Error.WriteLine($"fetching {name} @ {reference} ...");
        var cloneStarted = Stopwatch.GetTimestamp();
        var clone = Run(
            "git",
            ["clone", "--quiet", "--depth", "1", "--branch", reference, url, target],
            timeoutMs: 120_000);
        if (clone.Code == 0)
        {
            Console.Error.WriteLine(
                $"fetched {name} @ {reference} in "
                + $"{Stopwatch.GetElapsedTime(cloneStarted).TotalMilliseconds:F0} ms");
            return target;
        }

        // A bare commit SHA is not a branch: init, fetch the SHA, check it out.
        ForceRemoveTree(target);
        Directory.CreateDirectory(target);
        (string[] Arguments, string? WorkingDirectory)[] steps =
        [
            (["init", "--quiet"], target),
            (["remote", "add", "origin", url], target),
            (["fetch", "--quiet", "--depth", "1", "origin", reference], target),
            (["checkout", "--quiet", "FETCH_HEAD"], target),
        ];
        foreach (var (arguments, workingDirectory) in steps)
        {
            var done = Run("git", arguments, workingDirectory, timeoutMs: 60_000);
            if (done.Code == 0)
            {
                continue;
            }

            var detail = (string.IsNullOrWhiteSpace(done.Stderr) ? done.Stdout : done.Stderr).Trim();
            ForceRemoveTree(target);
            throw new ResolverException($"could not fetch source '{name}' ({repo} @ {reference}): {detail}");
        }

        return target;
    }

    public (IReadOnlyList<string>? Tags, string? Error) RemoteTags(string repo)
    {
        if (string.IsNullOrEmpty(repo))
        {
            return (null, "source declares no repo");
        }

        var url = Url(repo);
        if (offline && !url.StartsWith("file://", StringComparison.Ordinal))
        {
            return (null, "offline");
        }

        Console.Error.WriteLine($"reading tags for {repo} ...");
        var started = Stopwatch.GetTimestamp();
        var done = Run("git", ["ls-remote", "--tags", "--refs", url], timeoutMs: TagTimeoutMs);
        if (done.Code != 0)
        {
            var lines = done.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var detail = lines.Length > 0 ? lines[0].Trim() : $"git ls-remote exited {done.Code}";
            return (null, detail);
        }

        var tags = new List<string>();
        foreach (var line in done.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            var tagRef = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();
            const string prefix = "refs/tags/";
            if (tagRef.StartsWith(prefix, StringComparison.Ordinal))
            {
                tags.Add(tagRef[prefix.Length..]);
            }
        }

        return (tags, null);
    }

    /// <summary>Where a repository at a ref is checked out; repository + ref are the identity.</summary>
    public string CacheDirectory(string repo, string reference)
    {
        var trimmed = repo.TrimEnd('/');
        var lastSegment = trimmed.Contains('/', StringComparison.Ordinal)
            ? trimmed[(trimmed.LastIndexOf('/') + 1)..]
            : trimmed;
        if (lastSegment.Length == 0)
        {
            lastSegment = "source";
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{repo}@{reference}")))
            .ToLowerInvariant()[..8];
        return Path.Combine(paths.CacheDirectory, $"{Slug(lastSegment)}-{Slug(reference)}-{digest}");
    }

    private static string Url(string repo) =>
        repo.Contains("://", StringComparison.Ordinal) || repo.StartsWith("git@", StringComparison.Ordinal)
            ? repo
            : $"https://github.com/{repo}.git";

    private static string Slug(string value) =>
        NonSlugCharacters().Replace(value.Replace(":", "__", StringComparison.Ordinal), "_");

    [GeneratedRegex(@"[^A-Za-z0-9._-]", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacters();

    private static (int Code, string Stdout, string Stderr) Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        int timeoutMs = 0)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The sidecar's stdin is the JSON-RPC pipe. A git subprocess must never inherit it:
            // a credential prompt can consume protocol lines and then sit until the timeout.
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "never";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            start.WorkingDirectory = workingDirectory;
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (1, string.Empty, $"could not run {fileName}");
        }

        if (process is null)
        {
            return (1, string.Empty, $"could not run {fileName}");
        }

        using (process)
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (timeoutMs > 0 && !process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return (1, string.Empty, "timed out");
            }

            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return (process.ExitCode, stdout.Result, stderr.Result);
        }
    }

    /// <summary>Deletes a tree even when git left read-only files behind (Windows).</summary>
    public static void ForceRemoveTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }
    }
}

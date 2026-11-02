namespace AiWorkspaceManager.TestUtilities;

/// <summary>Repository paths and real fixtures, resolved without relying on the test host's CWD.</summary>
public static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string Fixture(params string[] parts) =>
        Path.Combine([RepositoryRoot, "tests", "AiWorkspaceManager.TestUtilities", "Fixtures", .. parts]);

    /// <summary>The parity fixture: a workspace plus three local sources.</summary>
    public static string ParityRoot => Fixture("parity");

    public static string ParityWorkspace => Path.Combine(ParityRoot, "workspace");

    public static string ParityTemplate => Path.Combine(ParityRoot, "template");

    public static string Golden(string name) => Fixture("golden", name);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIWorkspaceManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("could not find the repository root from the test output folder");
    }
}

/// <summary>A disposable temp directory; tests never write into the repository's fixtures.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "aiwm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public string Write(string relativePath, string text)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public string CopyFixture(string source, string relativeDestination = "")
    {
        var destination = string.IsNullOrEmpty(relativeDestination)
            ? Root
            : Path.Combine(Root, relativeDestination);
        CopyDirectory(source, destination);
        return destination;
    }

    /// <summary>Copies the parity fixture so a test may mutate it.</summary>
    public string CopyParity() => CopyFixture(TestPaths.ParityRoot);

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(Root))
                {
                    return;
                }

                // git marks pack files read-only; a recursive delete cannot remove them on Windows.
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
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

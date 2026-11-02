namespace AiWorkspaceManager.Common;

/// <summary>Fetches a remote source at a pinned ref and reads its release tags.</summary>
public interface ISourceFetcher
{
    string Fetch(string name, string repo, string reference, bool refresh);

    (IReadOnlyList<string>? Tags, string? Error) RemoteTags(string repo);
}

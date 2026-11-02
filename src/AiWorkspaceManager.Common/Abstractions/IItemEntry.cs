namespace AiWorkspaceManager.Common;

/// <summary>What the hasher needs to know about an item or a lock entry.</summary>
public interface IItemEntry
{
    string Path { get; }

    string? SourcePath { get; }
}

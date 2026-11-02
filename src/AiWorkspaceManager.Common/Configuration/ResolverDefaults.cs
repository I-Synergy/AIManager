namespace AiWorkspaceManager.Common;

/// <summary>Values the resolver and the extension agree on.</summary>
public static class ResolverDefaults
{
    /// <summary>Resolver version reported by the CLI and the handshake.</summary>
    public const string Version = "0.1.0";

    public const string TemplateSource = "template";
    public const string TemplateRepo = "I-Synergy/AI";
    public const string LocalRef = "local";
    public const string License = "MIT";

    /// <summary>Item kinds, in the order every payload and folder walk uses.</summary>
    public static readonly IReadOnlyList<string> Kinds =
        ["skill", "agent", "instruction", "prompt", "plugin", "hook"];

    /// <summary>Manifest array that holds each kind's selection.</summary>
    public static readonly IReadOnlyDictionary<string, string> ManifestKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["skill"] = "skills",
            ["agent"] = "agents",
            ["instruction"] = "instructions",
            ["prompt"] = "prompts",
            ["plugin"] = "plugins",
            ["hook"] = "hooks",
        };

    /// <summary>Folder name for each kind inside a source and under <c>.ai/</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> KindDirectories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["skill"] = "skills",
            ["agent"] = "agents",
            ["instruction"] = "instructions",
            ["prompt"] = "prompts",
            ["plugin"] = "plugins",
            ["hook"] = "hooks",
        };

    /// <summary>How an installed item is named on disk, per kind.</summary>
    public static readonly IReadOnlyDictionary<string, string> KindSuffixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["skill"] = "",
            ["agent"] = ".md",
            ["instruction"] = ".instructions.md",
            ["prompt"] = ".prompt.md",
            ["plugin"] = "",
            ["hook"] = "",
        };

    /// <summary>AI clients whose platform folders are materialised.</summary>
    public static readonly IReadOnlyList<string> Clients = ["claude", "copilot", "reasonix"];
}

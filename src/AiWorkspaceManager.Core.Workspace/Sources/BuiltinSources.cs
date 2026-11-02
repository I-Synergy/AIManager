using System.Text.Json.Nodes;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Third-party sources offered without being written by hand. The declaration order decides who
/// wins a name collision (<c>aspire</c> exists in both <c>aspire-skills</c> and
/// <c>awesome-copilot</c>; the official one keeps the name).
/// </summary>
public static class BuiltinSources
{
    public static IReadOnlyList<string> Order { get; } =
        ["aspire-skills", "awesome-copilot", "dotnet-maui", "dotnet-maui-tooling"];

    public static int IndexOf(string name)
    {
        var index = Order.ToList().IndexOf(name);
        return index < 0 ? Order.Count : index;
    }

    /// <summary>A fresh entry for one built-in source, or null for an unknown name.</summary>
    public static JsonObject? Create(string name) => name switch
    {
        "aspire-skills" => Remote(
            "microsoft/aspire-skills",
            ("skill", null)),
        "awesome-copilot" => Remote(
            "github/awesome-copilot",
            ("skill", null)),
        "dotnet-maui" => Remote(
            "dotnet/maui-labs",
            ("skill", ["plugins/dotnet-maui/skills"]),
            ("agent", [".github/agents"]),
            ("instruction", [".github/instructions"]),
            ("plugin", []),
            ("hook", [])),
        "dotnet-maui-tooling" => Remote(
            "dotnet/maui-labs",
            ("skill", ["plugins/dotnet-maui-tooling/skills"]),
            ("agent", []),
            ("instruction", []),
            ("plugin", []),
            ("hook", [])),
        _ => null,
    };

    public static IEnumerable<string> Names => Order;

    private static JsonObject Remote(
        string repo,
        params (string Kind, IReadOnlyList<string>? Paths)[] paths)
    {
        var entry = new JsonObject
        {
            ["repo"] = repo,
            ["ref"] = "main",
            ["resolve"] = "remote",
            ["license"] = "MIT",
        };

        var withPaths = paths.Where(pair => pair.Paths is not null).ToList();
        if (withPaths.Count > 0)
        {
            var pathsObject = new JsonObject();
            foreach (var (kind, values) in withPaths)
            {
                pathsObject[kind] = new JsonArray(values!.Select(value => (JsonNode)value).ToArray());
            }

            entry["paths"] = pathsObject;
        }

        return entry;
    }
}

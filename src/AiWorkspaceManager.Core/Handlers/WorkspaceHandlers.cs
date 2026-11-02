using System.Text.Json;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Protocol;
using AiWorkspaceManager.Core.Workspace;

namespace AiWorkspaceManager.Core.Handlers;

/// <summary>
/// RPC surface. One method per user-level operation; every method takes the workspace root the
/// host validated and returns the payload the webview already expects.
/// </summary>
public sealed class WorkspaceHandlers
{
    private readonly WorkspaceServices services;

    public WorkspaceHandlers(WorkspaceServices services)
    {
        this.services = services;
    }

    public IReadOnlyDictionary<string, RpcHandler> Create()
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            ["getBrowse"] = Run(parameters => Browse(parameters)),
            ["getPresets"] = Run(parameters => Session(parameters).Queries.PresetPayload()),
            ["getUpdates"] = Run(parameters => Session(parameters).Queries.Updates()),
            ["getReleases"] = Run(parameters => Session(parameters).Queries.Release()),
            ["install"] = Run(parameters => Install(parameters)),
            ["remove"] = Run(parameters => Remove(parameters)),
            ["sync"] = Run(parameters => Sync(parameters)),
            ["updateItems"] = Run(parameters => UpdateItems(parameters)),
            ["setLocked"] = Run(parameters => SetLocked(parameters)),
            ["addPreset"] = Run(parameters => AddPreset(parameters)),
            ["pruneDeselected"] = Run(parameters => PruneDeselected(parameters)),
            ["writeNotice"] = Run(parameters => Session(parameters).Maintenance.Notice().Result),
            ["rebuildIndex"] = Run(parameters => RebuildIndex(parameters)),
            ["addScaffold"] = Run(parameters => Session(parameters).Maintenance.Scaffold().Result),
            ["setClients"] = Run(parameters => SetClients(parameters)),
            ["adopt"] = Run(parameters => Session(parameters).Maintenance.Adopt().Payload),
            ["bootstrap"] = Run(parameters => Session(parameters).Maintenance.Bootstrap().Payload),
            ["getProject"] = Run(parameters => Session(parameters).Project.Read()),
            ["describeProject"] = Run(parameters => DescribeProject(parameters)),
            ["upgrade"] = Run(parameters => Upgrade(parameters)),
            ["vendorTooling"] = Run(parameters =>
                Session(parameters).Maintenance.VendorTooling(Bool(parameters, "overwrite")).Payload),
        };
        return handlers;
    }

    /// <summary>
    /// Runs a synchronous domain operation off the protocol read loop. Without this, a 500 ms
    /// browse would block reading the next request (including <c>cancel</c>); the transport itself
    /// is fully async.
    /// </summary>
    private static RpcHandler Run(Func<JsonElement, object?> handler) =>
        async (parameters, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => handler(parameters), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        };

    private BrowsePayload Browse(JsonElement parameters)
    {
        var session = Session(parameters);
        var refresh = Bool(parameters, "refresh");
        return session.Queries.Browse(refresh);
    }

    private CliResult Install(JsonElement parameters)
    {
        var session = Session(parameters);
        return session.Operations.Install(Ids(parameters)).Result;
    }

    private CliResult Remove(JsonElement parameters)
    {
        var session = Session(parameters);
        return session.Operations.RemoveSelection(Ids(parameters), Bool(parameters, "prune")).Result;
    }

    private CliResult Sync(JsonElement parameters) => Session(parameters).Operations.Sync().Result;

    private CliResult UpdateItems(JsonElement parameters)
    {
        var session = Session(parameters);
        var ids = OptionalIds(parameters);
        return session.Operations.ApplyUpdates(ids, Bool(parameters, "force")).Result;
    }

    private CliResult SetLocked(JsonElement parameters)
    {
        var session = Session(parameters);
        return session.Operations.SetLocked(Ids(parameters), Bool(parameters, "locked")).Result;
    }

    private CliResult AddPreset(JsonElement parameters)
    {
        var session = Session(parameters);
        return session.Operations.AddPresetAndSync(RequiredString(parameters, "name")).Result;
    }

    private CliResult PruneDeselected(JsonElement parameters) =>
        Session(parameters).Operations.PruneDeselected().Result;

    private CliResult RebuildIndex(JsonElement parameters) =>
        Session(parameters).Maintenance.Index(compact: true).Result;

    private CliResult SetClients(JsonElement parameters)
    {
        var session = Session(parameters);
        var clients = StringArray(parameters, "clients");
        _ = session.Maintenance.Clients(clients);
        return session.Operations.Sync().Result;
    }

    private ProjectInfoPayload DescribeProject(JsonElement parameters)
    {
        var session = Session(parameters);
        var current = session.Project.Read();
        var name = OptionalString(parameters, "name") ?? current.Name;
        var intention = OptionalString(parameters, "intention") ?? current.Intention;
        return session.Project.Describe(name, intention);
    }

    private CliResult Upgrade(JsonElement parameters)
    {
        var session = Session(parameters);
        return session.Operations.Upgrade(RequiredString(parameters, "release")).Result;
    }

    private WorkspaceSession Session(JsonElement parameters) =>
        services.Session(RequiredString(parameters, "root"));

    private static string RequiredString(JsonElement parameters, string name) =>
        OptionalString(parameters, name)
        ?? throw new RpcException(RpcErrorCodes.InvalidParams, $"'{name}' is required.");

    private static string? OptionalString(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool Bool(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static List<string> Ids(JsonElement parameters)
    {
        var ids = StringArray(parameters, "ids");
        if (ids.Count == 0)
        {
            throw new RpcException(RpcErrorCodes.InvalidParams, "'ids' must contain at least one id.");
        }

        return ids;
    }

    private static List<string>? OptionalIds(JsonElement parameters)
    {
        var ids = StringArray(parameters, "ids");
        return ids.Count == 0 ? null : ids;
    }

    private static List<string> StringArray(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var text = entry.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }
}

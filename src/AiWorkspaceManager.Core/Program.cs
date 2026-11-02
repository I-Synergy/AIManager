// Composition root for the sidecar. No logic here: register handler groups, then run the server.
//
// Launched by the extension host as:  dotnet <extensionPath>/dist/core/AiWorkspaceManager.Core.dll
// stdin/stdout carry protocol frames; stderr is forwarded to the extension's output channel.

using System.Text;
using AiWorkspaceManager.Core.Handlers;
using AiWorkspaceManager.Core.Protocol;
using AiWorkspaceManager.Core.Workspace;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var services = new WorkspaceServices();
var handlers = new WorkspaceHandlers(services).Create();

using var server = new NdjsonServer(Console.In, Console.Out, Console.Error, handlers);
return await server.RunAsync(CancellationToken.None).ConfigureAwait(false);

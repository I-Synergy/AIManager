using System.Text.Json;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>One preset entry: an id, or <c>{"all": true}</c> for every item of that kind.</summary>
public sealed record PresetEntry(string? Id, bool All);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVPC.Core;

[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class WorkspaceState
{
    public List<MediaAsset> Media { get; set; } = [];
    public List<PlaybackScheme> Schemes { get; set; } = [];
}

public static class WorkspacePersistence
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(WorkspaceState workspace) => JsonSerializer.Serialize(workspace, Options);

    public static WorkspaceState Deserialize(string json) =>
        JsonSerializer.Deserialize<WorkspaceState>(json, Options) ?? new WorkspaceState();
}

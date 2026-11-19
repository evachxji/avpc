using AVPC.Core;

namespace AVPC.Services;

public static class WorkspaceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AVPC", "workspace.json");

    public static WorkspaceState Load()
    {
        if (!File.Exists(FilePath)) return new WorkspaceState();
        return WorkspacePersistence.Deserialize(File.ReadAllText(FilePath));
    }

    public static void Save(WorkspaceState workspace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, WorkspacePersistence.Serialize(workspace));
        File.Move(temp, FilePath, true);
    }
}

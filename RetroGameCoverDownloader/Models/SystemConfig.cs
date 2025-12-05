namespace RetroGameCoverDownloader.Models;

public class SystemConfig
{
    public string SystemName { get; }
    public string Owner { get; }
    public string Repo { get; }
    public string FolderPath { get; }

    public SystemConfig(string systemName, string owner, string repo, string folderPath)
    {
        SystemName = systemName;
        Owner = owner;
        Repo = repo;
        FolderPath = folderPath;
    }
}

namespace RetroGameCoverDownloader.Models;

public class CoverDownloadItem
{
    public string GameName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string TargetFilename { get; set; } = "";
}
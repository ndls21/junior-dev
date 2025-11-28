namespace JuniorDev.VcsGit;

public class VcsConfig
{
    public string RepoPath { get; set; } = "";
    public string? RemoteUrl { get; set; }
    public bool AllowPush { get; set; } = false;
    public bool DryRun { get; set; } = false;
    public bool IsIntegrationTest { get; set; } = false;
}

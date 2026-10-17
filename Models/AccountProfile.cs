using System;

namespace GameLauncher.Models;

public class AccountProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public string AccountHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    public string AssociatedGuid { get; set; } = string.Empty;
    public string AssociatedMac { get; set; } = string.Empty;
    public string CustomNotes { get; set; } = string.Empty;
    public bool HasSettingsBinary { get; set; } = true;
}

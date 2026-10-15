namespace AutoPresser.Models;

/// <summary>The complete persisted configuration for AutoPresser.</summary>
public sealed class AutoPresserConfiguration
{
    public int SchemaVersion { get; set; } = 1;

    public Guid ActiveProfileId { get; set; }

    public List<ProfileConfiguration> Profiles { get; set; } = [];
}

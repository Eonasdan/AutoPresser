namespace AutoPresser.Models;

/// <summary>A named collection of bindings.</summary>
public sealed class ProfileConfiguration
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Default";

    public List<BindingConfiguration> Bindings { get; set; } = [];
}

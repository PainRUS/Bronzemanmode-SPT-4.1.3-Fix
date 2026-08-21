using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Bronzeman;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.randek.bronzeman";
    public string Name { get; init; } = "Bronzeman";
    public string Author { get; init; } = "Randek";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("2.0.2");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.3");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}

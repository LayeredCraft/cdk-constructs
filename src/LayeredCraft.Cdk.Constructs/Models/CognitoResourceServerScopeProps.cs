namespace LayeredCraft.Cdk.Constructs.Models;

public interface ICognitoResourceServerScopeProps
{
    string Name { get; }

    string Description { get; }
}

public sealed record CognitoResourceServerScopeProps : ICognitoResourceServerScopeProps
{
    public required string Name { get; init; }

    public required string Description { get; init; }
}
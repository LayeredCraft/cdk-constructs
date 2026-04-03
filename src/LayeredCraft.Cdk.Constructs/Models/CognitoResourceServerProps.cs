namespace LayeredCraft.Cdk.Constructs.Models;

public interface ICognitoResourceServerProps
{
    string Name { get; }

    string Identifier { get; }

    IReadOnlyList<ICognitoResourceServerScopeProps> Scopes { get; }
    
}
public sealed record CognitoResourceServerProps : ICognitoResourceServerProps
{
    public required string Name { get; init; }

    public required string Identifier { get; init; }

    public required IReadOnlyList<ICognitoResourceServerScopeProps> Scopes { get; init; }
}
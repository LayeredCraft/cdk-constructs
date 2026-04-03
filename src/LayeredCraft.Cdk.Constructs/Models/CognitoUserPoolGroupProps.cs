namespace LayeredCraft.Cdk.Constructs.Models;

public interface ICognitoUserPoolGroupProps
{
    string Name { get; }
    string? Description { get; }
    int? Precedence { get; }
    string? RoleArn { get; }
}

public sealed record CognitoUserPoolGroupProps(
    string Name,
    string? Description = null,
    int? Precedence = null,
    string? RoleArn = null) : ICognitoUserPoolGroupProps;
namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// The attributes used to import an existing Web Function endpoint.
/// </summary>
public sealed record WebFunctionEndpointAttributes
{
    /// <summary>The endpoint ARN.</summary>
    public required string EndpointArn { get; init; }

    /// <summary>The endpoint domain name, without a scheme.</summary>
    public required string DomainName { get; init; }

    /// <summary>How the endpoint authenticates requests.</summary>
    public required WebFunctionAuthType AuthType { get; init; }

    /// <summary>The regional scope. Defaults to <see cref="WebFunctionEndpointType.HomeRegion"/>.</summary>
    public WebFunctionEndpointType EndpointType { get; init; } = WebFunctionEndpointType.HomeRegion;
}

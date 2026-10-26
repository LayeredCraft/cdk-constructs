using System.Collections.Generic;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Configuration for a Web Function endpoint.
/// </summary>
public interface IWebFunctionEndpointOptions
{
    /// <summary>The endpoint name. Defaults to <c>default</c>; must be unique within the function.</summary>
    string EndpointName { get; }

    /// <summary>How the endpoint authenticates requests. Required; there is no default.</summary>
    WebFunctionAuthType AuthType { get; }

    /// <summary>The regional scope. Defaults to <see cref="WebFunctionEndpointType.HomeRegion"/>.</summary>
    WebFunctionEndpointType EndpointType { get; }

    /// <summary>The Regions to serve. Required for multi- and per-Region endpoints.</summary>
    IReadOnlyList<string> Regions { get; }

    /// <summary>The maximum number of environments. Must be 2–10000 when set.</summary>
    int? MaxEnvironments { get; }

    /// <summary>
    /// The per-endpoint request throttle. Must be 0, a multiple of 100 between 100 and 1000, or a multiple of
    /// 1000 between 2000 and 10000 when set.
    /// </summary>
    int? RateLimit { get; }

    /// <summary>The endpoint description.</summary>
    string? Description { get; }
}

/// <summary>
/// Configuration for a Web Function endpoint.
/// </summary>
public sealed record WebFunctionEndpointOptions : IWebFunctionEndpointOptions
{
    /// <inheritdoc />
    public string EndpointName { get; init; } = "default";

    /// <inheritdoc />
    public required WebFunctionAuthType AuthType { get; init; }

    /// <inheritdoc />
    public WebFunctionEndpointType EndpointType { get; init; } = WebFunctionEndpointType.HomeRegion;

    /// <inheritdoc />
    public IReadOnlyList<string> Regions { get; init; } = [];

    /// <inheritdoc />
    public int? MaxEnvironments { get; init; }

    /// <inheritdoc />
    public int? RateLimit { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }
}

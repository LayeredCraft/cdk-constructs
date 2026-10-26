namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// The regional scope of a Web Function endpoint.
/// </summary>
public enum WebFunctionEndpointType
{
    /// <summary>The endpoint serves only the function's home Region.</summary>
    HomeRegion,

    /// <summary>The endpoint serves every configured Region with automatic routing.</summary>
    MultiRegion,

    /// <summary>The endpoint serves the configured Regions independently.</summary>
    PerRegion,
}

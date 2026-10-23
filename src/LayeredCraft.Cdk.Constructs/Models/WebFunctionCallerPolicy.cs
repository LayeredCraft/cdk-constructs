namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Who maintains a caller's identity policy when granting invoke access to a Web Function endpoint.
/// </summary>
public enum WebFunctionCallerPolicy
{
    /// <summary>
    /// The library must fully configure invocation within this deployment, or synthesis fails.
    /// </summary>
    Automatic,

    /// <summary>
    /// The caller's identity policy is configured outside this app. The library writes only the endpoint's
    /// resource-policy statement.
    /// </summary>
    ExternallyManaged,
}

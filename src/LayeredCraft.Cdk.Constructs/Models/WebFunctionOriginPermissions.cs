namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Who maintains the CloudFront statement in the endpoint's resource policy.
/// </summary>
public enum WebFunctionOriginPermissions
{
    /// <summary>
    /// When the origin is bound to a distribution, add an <c>aws:SourceArn</c>-scoped statement for that
    /// distribution to the function's resource policy. Requires an endpoint owned by a
    /// <c>WebFunctionConstruct</c> in the distribution's stack.
    /// </summary>
    Automatic,

    /// <summary>
    /// The consumer guarantees that the function's resource policy already allows this distribution (for
    /// example, an imported endpoint, or an explicit <c>GrantCloudFrontInvoke</c>).
    /// </summary>
    ExternallyManaged,
}

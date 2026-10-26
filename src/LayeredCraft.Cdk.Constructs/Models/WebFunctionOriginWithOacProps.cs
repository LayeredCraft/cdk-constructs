using Amazon.CDK.AWS.CloudFront;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Origin settings for an <see cref="WebFunctionAuthType.IamAuth"/> endpoint signed by an origin access
/// control.
/// </summary>
public sealed record WebFunctionOriginWithOacProps : WebFunctionOriginProps
{
    /// <summary>
    /// An existing origin access control (for example, one shared across distributions). When null, one is
    /// created in the binding distribution's scope. A supplied origin access control is used as is; the
    /// consumer is responsible for its configuration.
    /// </summary>
    public IOriginAccessControl? OriginAccessControl { get; init; }

    /// <summary>
    /// Who maintains the CloudFront statement in the endpoint's resource policy. Defaults to
    /// <see cref="WebFunctionOriginPermissions.Automatic"/>.
    /// </summary>
    public WebFunctionOriginPermissions Permissions { get; init; } = WebFunctionOriginPermissions.Automatic;
}

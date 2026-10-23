using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// A Web Function endpoint. Owned endpoints are created by <see cref="WebFunctionConstruct"/> and own the
/// function's single resource policy; imported endpoints are created with
/// <see cref="WebFunctionEndpointConstruct.FromEndpointAttributes"/> and own no resources, so their resource policy
/// stays externally owned. Invoke grants are endpoint-scoped and always target <c>lambda:InvokeWebFunctionEndpoint</c>
/// on this endpoint's ARN.
/// </summary>
public interface IWebFunctionEndpoint : IResource
{
    /// <summary>The endpoint name.</summary>
    string EndpointName { get; }

    /// <summary>The endpoint ARN.</summary>
    string EndpointArn { get; }

    /// <summary>The endpoint domain name.</summary>
    string DomainName { get; }

    /// <summary>The endpoint URL.</summary>
    string Url { get; }

    /// <summary>How the endpoint authenticates requests.</summary>
    WebFunctionAuthType AuthType { get; }

    /// <summary>The regional scope of the endpoint.</summary>
    WebFunctionEndpointType EndpointType { get; }

    /// <summary>
    /// Grants a principal permission to invoke this endpoint. By default the library writes the caller's identity
    /// policy only when the caller's account can be established in this deployment; otherwise the grant fails closed.
    /// Service principals, wildcard principals, account principals, and principals wrapped with conditions are
    /// rejected.
    /// </summary>
    /// <param name="grantee">The principal that may invoke the endpoint.</param>
    /// <returns>The grant describing the statements that were added.</returns>
    Grant GrantInvoke(IGrantable grantee);

    /// <summary>
    /// Grants a principal permission to invoke this endpoint, controlling who maintains the caller's identity
    /// policy. With <see cref="WebFunctionCallerPolicy.Automatic"/> the library writes the identity policy when the
    /// caller's account is established; with <see cref="WebFunctionCallerPolicy.ExternallyManaged"/> it writes only
    /// an endpoint-scoped resource-policy statement and leaves the caller's identity policy to the caller's own
    /// account.
    /// </summary>
    /// <param name="grantee">The principal that may invoke the endpoint.</param>
    /// <param name="options">The grant options.</param>
    /// <returns>The grant describing the statements that were added.</returns>
    Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options);

    /// <summary>Allows a CloudFront distribution to invoke this endpoint.</summary>
    /// <param name="distributionArn">The CloudFront distribution ARN, literal or token.</param>
    void GrantCloudFrontInvoke(string distributionArn);
}

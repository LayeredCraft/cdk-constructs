using Amazon.CDK;
using Amazon.CDK.AWS.IAM;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// A Web Function endpoint. Owned endpoints are created by <see cref="WebFunctionConstruct"/>; imported
/// endpoints are created with <c>WebFunctionEndpointConstruct.FromEndpointAttributes</c> and own no resources.
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
    /// Grants a principal permission to invoke this endpoint, configuring the caller's identity policy when the
    /// caller's account is established in this deployment.
    /// </summary>
    /// <param name="grantee">The principal that may invoke the endpoint.</param>
    /// <returns>The grant describing the statements that were added.</returns>
    Grant GrantInvoke(IGrantable grantee);

    /// <summary>
    /// Grants a principal permission to invoke this endpoint, controlling who maintains the caller's identity
    /// policy.
    /// </summary>
    /// <param name="grantee">The principal that may invoke the endpoint.</param>
    /// <param name="options">The grant options.</param>
    /// <returns>The grant describing the statements that were added.</returns>
    Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options);

    /// <summary>Allows a CloudFront distribution to invoke this endpoint.</summary>
    /// <param name="distributionArn">The CloudFront distribution ARN, literal or token.</param>
    void GrantCloudFrontInvoke(string distributionArn);
}

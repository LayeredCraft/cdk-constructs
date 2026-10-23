using System;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// An imported Web Function endpoint. It owns no AWS resources, so it can only configure a caller's identity
/// policy (when the caller's account is established) and never touches the function's resource policy.
/// </summary>
internal sealed class WebFunctionImportedEndpoint : Resource, IWebFunctionEndpoint
{
    private readonly string? _account;

    internal WebFunctionImportedEndpoint(Construct scope, string id, WebFunctionEndpointAttributes attributes)
        : base(scope, id)
    {
        EndpointArn = attributes.EndpointArn;
        DomainName = attributes.DomainName;
        AuthType = attributes.AuthType;
        EndpointType = attributes.EndpointType;
        EndpointName = ParseEndpointName(attributes.EndpointArn);
        _account = ParseAccount(attributes.EndpointArn);
    }

    /// <inheritdoc />
    public string EndpointName { get; }

    /// <inheritdoc />
    public string EndpointArn { get; }

    /// <inheritdoc />
    public string DomainName { get; }

    /// <inheritdoc />
    public string Url => $"https://{DomainName}";

    /// <inheritdoc />
    public WebFunctionAuthType AuthType { get; }

    /// <inheritdoc />
    public WebFunctionEndpointType EndpointType { get; }

    /// <inheritdoc />
    public Grant GrantInvoke(IGrantable grantee) => GrantInvoke(grantee, new WebFunctionInvokeGrantOptions());

    /// <inheritdoc />
    public Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options)
        => WebFunctionGrants.GrantInvoke(
            AuthType,
            grantee,
            EndpointName,
            EndpointArn,
            _account,
            options,
            this,
            resourcePolicyOwner: null);

    /// <inheritdoc />
    public void GrantCloudFrontInvoke(string distributionArn)
        => throw new InvalidOperationException(
            "The resource policy of an imported Web Function endpoint is externally owned, so CloudFront invoke " +
            "access cannot be granted here. Configure the resource policy in its owning account.");

    private static string ParseEndpointName(string arn)
    {
        if (Token.IsUnresolved(arn))
            return string.Empty;

        const string marker = "endpoint/";
        var index = arn.LastIndexOf(marker, StringComparison.Ordinal);
        return index >= 0 ? arn[(index + marker.Length)..] : string.Empty;
    }

    private static string? ParseAccount(string arn)
    {
        if (Token.IsUnresolved(arn))
            return null;

        var parts = arn.Split(':');
        return parts.Length >= 6 ? parts[4] : null;
    }
}

using System;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.JSII.Runtime.Deputy;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// A CloudFront origin for an <see cref="WebFunctionAuthType.IamAuth"/> Web Function endpoint, signed by an origin
/// access control. The origin access control and the endpoint's CloudFront invocation statement are created when the
/// distribution binds the origin, so the distribution's identity is available.
/// </summary>
internal sealed class WebFunctionOacOrigin : DeputyBase, IOrigin
{
    private const string OriginAccessControlChildId = "WebFunctionOac";

    private readonly IWebFunctionEndpoint _endpoint;
    private readonly WebFunctionOriginWithOacProps _props;

    internal WebFunctionOacOrigin(IWebFunctionEndpoint endpoint, WebFunctionOriginWithOacProps props)
        : base(new DeputyProps(Array.Empty<object>()))
    {
        _endpoint = endpoint;
        _props = props;
    }

    /// <inheritdoc />
    public IOriginBindConfig Bind(Construct scope, IOriginBindOptions options)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(options);

        var originAccessControl = _props.OriginAccessControl ?? new FunctionUrlOriginAccessControl(
            scope,
            OriginAccessControlChildId,
            new FunctionUrlOriginAccessControlProps
            {
                Signing = ResolveSigning(_endpoint.EndpointType),
            });

        var origin = new HttpOrigin(
            _endpoint.DomainName,
            WebFunctionOrigin.BuildHttpOriginProps(_props, originAccessControl.OriginAccessControlId));

        ApplyPermissions(scope, options);

        return origin.Bind(scope, options);
    }

    private static Signing ResolveSigning(WebFunctionEndpointType endpointType)
        => endpointType == WebFunctionEndpointType.MultiRegion
            ? new Signing(SigningProtocol.SIGV4A, SigningBehavior.ALWAYS_AMZ_AUTH)
            : Signing.SIGV4_ALWAYS_AMZ_AUTH;

    private void ApplyPermissions(Construct scope, IOriginBindOptions options)
    {
        if (_props.Permissions == WebFunctionOriginPermissions.ExternallyManaged)
        {
            Annotations.Of(scope).AddInfo(BuildExternallyManagedAnnotation(scope));
            return;
        }

        if (_endpoint is not WebFunctionEndpointConstruct)
        {
            throw new InvalidOperationException(
                "The origin cannot manage the resource policy of an imported Web Function endpoint. Use " +
                "Permissions = WebFunctionOriginPermissions.ExternallyManaged and grant the distribution in the " +
                "owner's policy.");
        }

        var stack = Stack.Of(scope);
        if (!ReferenceEquals(stack, Stack.Of(_endpoint)))
        {
            throw new InvalidOperationException(
                "The distribution and the Web Function must be in the same stack for automatic origin permissions; " +
                "otherwise CloudFormation has a dependency cycle (the distribution reads the endpoint domain, the " +
                "policy reads the distribution id). Use ExternallyManaged and " +
                "endpoint.GrantCloudFrontInvoke(<literal distribution ARN>).");
        }

        var distributionArn = stack.FormatArn(new ArnComponents
        {
            Service = "cloudfront",
            Region = "",
            Resource = "distribution",
            ResourceName = options.DistributionId,
        });

        _endpoint.GrantCloudFrontInvoke(distributionArn);
    }

    private string BuildExternallyManagedAnnotation(Construct scope)
    {
        var distribution = DescribeDistribution(scope);

        if (_endpoint is WebFunctionEndpointConstruct)
        {
            return $"CloudFront invoke permissions for the '{_endpoint.Node.Path}' endpoint are externally managed. " +
                   $"Grant the distribution '{distribution}' access by calling " +
                   "endpoint.GrantCloudFrontInvoke(<distribution ARN>) in the endpoint's stack; that adds an " +
                   "endpoint-scoped statement for the 'cloudfront.amazonaws.com' service principal with a " +
                   "StringEquals 'aws:SourceArn' condition.";
        }

        return $"CloudFront invoke permissions for the imported endpoint {DescribeImportedEndpoint()} are externally " +
               "managed and cannot be modified here. The Web Function owner must add an endpoint-scoped " +
               "resource-policy statement that allows the 'cloudfront.amazonaws.com' service principal to invoke " +
               $"this endpoint, guarded by a StringEquals condition on 'aws:SourceArn' for the distribution " +
               $"'{distribution}'.";
    }

    private string DescribeImportedEndpoint()
    {
        if (!Token.IsUnresolved(_endpoint.EndpointArn))
            return $"'{_endpoint.EndpointArn}'";

        return _endpoint.EndpointName.Length > 0
            ? $"named '{_endpoint.EndpointName}' (domain '{_endpoint.DomainName}')"
            : $"with domain '{_endpoint.DomainName}'";
    }

    private static string DescribeDistribution(Construct scope)
        => scope.Node.Scope?.Node.Path ?? scope.Node.Path;
}

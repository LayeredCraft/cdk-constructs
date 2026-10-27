using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// A Web Function endpoint. Owns a single <c>AWS::Lambda::WebFunctionEndpoint</c> whose logical child id is
/// <c>Resource</c>. Instances are created by <see cref="WebFunctionConstruct"/>.
/// </summary>
public sealed class WebFunctionEndpointConstruct : Resource, IWebFunctionEndpoint, IResourceWithPolicyV2
{
    private readonly CfnWebFunctionEndpoint _resource;

    internal WebFunctionEndpointConstruct(
        Construct scope,
        string id,
        WebFunctionConstruct webFunction,
        IWebFunctionEndpointOptions options)
        : base(scope, id)
    {
        ArgumentNullException.ThrowIfNull(webFunction);
        ArgumentNullException.ThrowIfNull(options);

        WebFunctionValidation.ValidateEndpointName(options.EndpointName);
        WebFunctionValidation.ValidateRegions(options.EndpointType, options.Regions, Stack.Of(webFunction).Region);
        WebFunctionValidation.ValidateMaxEnvironments(options.MaxEnvironments);
        WebFunctionValidation.ValidateRateLimit(options.RateLimit);

        WebFunction = webFunction;
        EndpointName = options.EndpointName;
        AuthType = options.AuthType;
        EndpointType = options.EndpointType;
        Regions = options.Regions;

        _resource = new CfnWebFunctionEndpoint(this, "Resource", new CfnWebFunctionEndpointProps
        {
            AuthType = options.AuthType.ToString(),
            EndpointName = options.EndpointName,
            EndpointType = options.EndpointType.ToString(),
            FunctionName = webFunction.FunctionName,
            Description = options.Description,
            Regions = options.Regions.Count > 0 ? options.Regions.ToArray() : null,
            RevisionWeights = new CfnWebFunctionEndpoint.IRevisionWeightProperty[]
            {
                new CfnWebFunctionEndpoint.RevisionWeightProperty
                {
                    RevisionId = webFunction.RevisionId,
                    Weight = 100,
                },
            },
            ScalingConfig = options.MaxEnvironments.HasValue
                ? new CfnWebFunctionEndpoint.ScalingConfigProperty { MaxEnvironments = options.MaxEnvironments.Value }
                : null,
            ThrottleConfig = options.RateLimit.HasValue
                ? new CfnWebFunctionEndpoint.ThrottleConfigProperty { RateLimit = options.RateLimit.Value }
                : null,
        });

        Node.DefaultChild = _resource;

        EmitRemoteCompanionWarning(webFunction);
    }

    private const string RemoteCompanionWarningId =
        "@layeredcraft/cdk-constructs:webFunctionRemoteCompanionLogGroups";

    private void EmitRemoteCompanionWarning(WebFunctionConstruct webFunction)
    {
        if (EndpointType is not (WebFunctionEndpointType.PerRegion or WebFunctionEndpointType.MultiRegion))
            return;

        var modeSentence = webFunction.Logs switch
        {
            WebFunctionLogsCreate =>
                "In the stack Region, the centralized group and its companion are managed by this stack.",
            WebFunctionLogsUse { Companion: not null } =>
                "In the stack Region, the caller-supplied companion is used.",
            WebFunctionLogsUse =>
                "In the stack Region, the companion is not managed either.",
            _ =>
                "All of this function's log groups, including the centralized group, are service-managed and never expire.",
        };

        var endpointLabel = Token.IsUnresolved(EndpointName)
            ? "This endpoint"
            : $"Endpoint '{EndpointName}'";

        Annotations.Of(this).AddWarningV2(
            RemoteCompanionWarningId,
            $"{endpointLabel} ({EndpointType}) runs requests in Regions other than the stack Region. " +
            $"Lambda creates {webFunction.DescribeCompanionLogGroup()} in each of those Regions on first request " +
            "with no expiry; this construct does not manage log groups outside the stack Region. " +
            $"Set their retention separately. {modeSentence}");
    }

    /// <summary>The Web Function this endpoint belongs to.</summary>
    public WebFunctionConstruct WebFunction { get; }

    /// <summary>The endpoint name.</summary>
    public string EndpointName { get; }

    /// <summary>How the endpoint authenticates requests.</summary>
    public WebFunctionAuthType AuthType { get; }

    /// <summary>The regional scope of the endpoint.</summary>
    public WebFunctionEndpointType EndpointType { get; }

    /// <summary>The Regions the endpoint serves.</summary>
    public IReadOnlyList<string> Regions { get; }

    /// <summary>The endpoint ARN.</summary>
    public string EndpointArn => _resource.AttrEndpointArn;

    /// <summary>The endpoint domain name.</summary>
    public string DomainName => _resource.AttrDomainName;

    /// <summary>The endpoint URL.</summary>
    public string Url => $"https://{DomainName}";

    /// <summary>
    /// Adds a statement to the Web Function's single resource policy. The statement must target exactly this
    /// endpoint's ARN.
    /// </summary>
    /// <param name="statement">The statement, scoped to <see cref="EndpointArn"/>.</param>
    /// <returns>The result of adding the statement.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="statement"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the statement does not target exactly this endpoint's ARN.</exception>
    public AddToResourcePolicyResult AddToResourcePolicy(PolicyStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var resources = statement.Resources;
        if (resources is null
            || resources.Length != 1
            || !string.Equals(resources[0], EndpointArn, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A statement added to a Web Function endpoint must target exactly that endpoint's ARN.",
                nameof(statement));
        }

        WebFunctionValidation.ValidateResourcePolicyStatement(statement);

        var stored = statement.Copy(null);
        stored.Freeze();

        var policy = WebFunction.AddResourcePolicyStatement(stored);

        return new AddToResourcePolicyResult
        {
            StatementAdded = true,
            PolicyDependable = policy,
        };
    }

    IAddToResourcePolicyResult IResourceWithPolicyV2.AddToResourcePolicy(PolicyStatement statement)
        => AddToResourcePolicy(statement);

    /// <summary>
    /// Allows a CloudFront distribution to invoke this endpoint. Only supported for endpoints that use
    /// <see cref="WebFunctionAuthType.IamAuth"/>.
    /// </summary>
    /// <param name="distributionArn">The CloudFront distribution ARN, literal or token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the endpoint does not use IAM authentication.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="distributionArn"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a literal <paramref name="distributionArn"/> is not a distribution ARN.</exception>
    public void GrantCloudFrontInvoke(string distributionArn)
    {
        if (AuthType != WebFunctionAuthType.IamAuth)
        {
            throw new InvalidOperationException(
                $"The '{EndpointName}' endpoint uses ApplicationManaged authentication, so CloudFront cannot invoke " +
                "it. Use an IamAuth endpoint.");
        }

        ArgumentNullException.ThrowIfNull(distributionArn);
        WebFunctionValidation.ValidateCloudFrontDistributionArn(distributionArn);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [EndpointArn],
            Principals = [new ServicePrincipal("cloudfront.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["aws:SourceArn"] = distributionArn,
                },
            },
        });

        AddToResourcePolicy(statement);
    }

    internal CfnWebFunctionEndpoint CfnEndpoint => _resource;

    /// <inheritdoc />
    public Grant GrantInvoke(IGrantable grantee) => GrantInvoke(grantee, new WebFunctionInvokeGrantOptions());

    /// <inheritdoc />
    public Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options)
        => WebFunctionGrants.GrantInvoke(
            AuthType,
            grantee,
            EndpointName,
            EndpointArn,
            Stack.Of(this).Account,
            options,
            this,
            this);

    /// <summary>Imports an existing Web Function endpoint that this app does not own.</summary>
    /// <param name="scope">The construct scope.</param>
    /// <param name="id">The construct id.</param>
    /// <param name="attributes">The endpoint attributes.</param>
    /// <returns>An endpoint that owns no resources and only supports identity-side invocation grants.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IWebFunctionEndpoint FromEndpointAttributes(
        Construct scope,
        string id,
        WebFunctionEndpointAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(attributes);

        return new WebFunctionImportedEndpoint(scope, id, attributes);
    }
}

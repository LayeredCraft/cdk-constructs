using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.Lambda;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// A Web Function endpoint. Owns a single <c>AWS::Lambda::WebFunctionEndpoint</c> whose logical child id is
/// <c>Resource</c>. Instances are created by <see cref="WebFunctionConstruct"/>.
/// </summary>
public sealed class WebFunctionEndpointConstruct : Resource
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

    internal CfnWebFunctionEndpoint CfnEndpoint => _resource;
}

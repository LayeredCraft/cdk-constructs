using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace WebFunctionSandbox;

/// <summary>
/// The sandbox workload stacks. Every workload is created through the public <see cref="WebFunctionConstruct"/>
/// API - no raw CloudFormation resources are used - so the sandbox exercises the same code path a consumer does.
/// </summary>
internal sealed class AppStack : Stack
{
    public AppStack(
        Construct scope,
        string id,
        IStackProps? props,
        SandboxConfiguration configuration,
        SandboxNaming naming,
        IBucket artifactsBucket)
        : base(scope, id, props)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(naming);
        ArgumentNullException.ThrowIfNull(artifactsBucket);

        naming.ApplyTags(this);

        // Imported by name: the app stack does not own this bucket, so every workload that reads from it must use
        // the ExternallyManaged bucket policy (the ExternalStack bucket policy supplies the read grant).
        var externalBucket = Bucket.FromBucketName(this, "ExternalArtifacts", naming.ExternalBucketName);

        // --- Next.js standalone: owned versioned bucket (Automatic policy) behind an OAC distribution. ---
        var nextJs = new WebFunctionConstruct(this, "NextJs", new WebFunctionConstructProps
        {
            FunctionName = naming.NextJsFunctionName,
            Code = WebFunctionCode.FromBucket(artifactsBucket, configuration.NextJs.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                ObjectVersion = configuration.NextJs.ObjectVersion,
            }),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NODE_ENV"] = "production",
                ["NEXT_TELEMETRY_DISABLED"] = "1",
            },
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Description = "Sandbox Next.js standalone Web Function",
        });

        var nextJsDistribution = CreateDistribution(
            this,
            "NextJsDistribution",
            nextJs.DefaultEndpoint,
            "Sandbox Next.js distribution (dynamic SSR default, immutable assets cached)",
            "/_next/static/*");

        // Optional second distribution on the same endpoint: exercises adding a second scoped resource-policy
        // statement, and (when removed) the statement lifecycle on the next deploy.
        Distribution? nextJsSecondDistribution = configuration.SecondDistribution
            ? CreateDistribution(
                this,
                "NextJsSecondDistribution",
                nextJs.DefaultEndpoint,
                "Sandbox second Next.js distribution (resource-policy lifecycle)",
                "/_next/static/*")
            : null;

        // --- ASP.NET Core: externally managed bucket. Default endpoint is invoke-granted; a second endpoint
        // exists to prove endpoint-scoped denial. ---
        var aspNetCore = new WebFunctionConstruct(this, "AspNetCore", new WebFunctionConstructProps
        {
            FunctionName = naming.AspNetCoreFunctionName,
            Code = WebFunctionCode.FromBucket(externalBucket, configuration.AspNetCore.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
                ObjectVersion = configuration.AspNetCore.ObjectVersion,
            }),
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
                MaxEnvironments = 10,
                RateLimit = 100,
            },
            Description = "Sandbox ASP.NET Core Web Function",
        });

        var aspNetCoreAlt = aspNetCore.AddEndpoint("AltEndpoint", new WebFunctionEndpointOptions
        {
            EndpointName = "alt",
            AuthType = WebFunctionAuthType.IamAuth,
            Description = "Second endpoint on the same function for endpoint-scoped denial checks",
        });

        // --- Blazor Static SSR: externally managed bucket behind an OAC distribution. ---
        var blazor = new WebFunctionConstruct(this, "Blazor", new WebFunctionConstructProps
        {
            FunctionName = naming.BlazorFunctionName,
            Code = WebFunctionCode.FromBucket(externalBucket, configuration.Blazor.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
                ObjectVersion = configuration.Blazor.ObjectVersion,
            }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Description = "Sandbox Blazor Static SSR Web Function",
        });

        var blazorDistribution = CreateDistribution(
            this,
            "BlazorDistribution",
            blazor.DefaultEndpoint,
            "Sandbox Blazor distribution (dynamic document, immutable framework assets cached)",
            "/_framework/*",
            "/_content/*");

        // --- Operator roles. ---
        // Granted caller: an owned mutable identity in the same stack, so the library writes an identity policy.
        var callerRole = new Role(this, "CallerRole", new RoleProps
        {
            AssumedBy = new AccountPrincipal(configuration.Account),
            Description = "Operator role granted invoke on the ASP.NET Core default endpoint (identity policy).",
        });
        aspNetCore.DefaultEndpoint.GrantInvoke(callerRole);

        // Denied caller: an owned role with no invoke grant - a signed request from it must be denied.
        var deniedRole = new Role(this, "DeniedRole", new RoleProps
        {
            AssumedBy = new AccountPrincipal(configuration.Account),
            Description = "Operator role with no invoke grant, used to observe a 403.",
        });

        // Imported callers: two roles owned by the external stack and imported by literal ARN. Because each is
        // imported (not an owned mutable identity) the library records the grant in the endpoint resource policy
        // only. One is imported with Mutable = false, the other with the default mutable setting, so both
        // resource-policy-only sub-cases in the acceptance matrix are synthesized.
        var immutableCaller = Role.FromRoleArn(this, "ImportedCallerImmutableRole", naming.ImportedCallerImmutableRoleArn, new FromRoleArnOptions
        {
            Mutable = false,
        });
        aspNetCore.DefaultEndpoint.GrantInvoke(immutableCaller);

        var mutableCaller = Role.FromRoleArn(this, "ImportedCallerMutableRole", naming.ImportedCallerMutableRoleArn, new FromRoleArnOptions
        {
            Mutable = true,
        });
        aspNetCore.DefaultEndpoint.GrantInvoke(mutableCaller);

        // Cross-account caller (optional): an explicitly ExternallyManaged resource-policy grant to a role in a
        // second sandbox account, per the approved design.
        if (configuration.CrossAccountCallerRoleArn is not null)
        {
            var crossAccountRole = Role.FromRoleArn(this, "CrossAccountCallerRole", configuration.CrossAccountCallerRoleArn, new FromRoleArnOptions
            {
                Mutable = false,
            });
            aspNetCore.DefaultEndpoint.GrantInvoke(crossAccountRole, new WebFunctionInvokeGrantOptions
            {
                CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
            });
        }

        // --- Outputs consumed by smoke.sh (read with aws cloudformation describe-stacks). ---
        Output(this, "SandboxId", configuration.SandboxId);
        Output(this, "SandboxAccount", configuration.Account);
        Output(this, "SandboxRegion", configuration.Region);
        Output(this, "ArtifactsBucketName", artifactsBucket.BucketName);
        Output(this, "ExternalBucketName", naming.ExternalBucketName);

        Output(this, "NextJsFunctionName", nextJs.FunctionName);
        Output(this, "NextJsEndpointUrl", nextJs.DefaultEndpoint.Url);
        Output(this, "NextJsEndpointDomain", nextJs.DefaultEndpoint.DomainName);
        Output(this, "NextJsEndpointArn", nextJs.DefaultEndpoint.EndpointArn);
        Output(this, "NextJsDistributionDomain", nextJsDistribution.DistributionDomainName);
        Output(this, "NextJsDistributionId", nextJsDistribution.DistributionId);
        if (nextJsSecondDistribution is not null)
        {
            Output(this, "NextJsSecondDistributionDomain", nextJsSecondDistribution.DistributionDomainName);
            Output(this, "NextJsSecondDistributionId", nextJsSecondDistribution.DistributionId);
        }

        Output(this, "AspNetCoreFunctionName", aspNetCore.FunctionName);
        Output(this, "AspNetCoreEndpointUrl", aspNetCore.DefaultEndpoint.Url);
        Output(this, "AspNetCoreEndpointDomain", aspNetCore.DefaultEndpoint.DomainName);
        Output(this, "AspNetCoreEndpointArn", aspNetCore.DefaultEndpoint.EndpointArn);
        Output(this, "AspNetCoreAltEndpointUrl", aspNetCoreAlt.Url);
        Output(this, "AspNetCoreAltEndpointDomain", aspNetCoreAlt.DomainName);
        Output(this, "AspNetCoreAltEndpointArn", aspNetCoreAlt.EndpointArn);

        Output(this, "BlazorFunctionName", blazor.FunctionName);
        Output(this, "BlazorEndpointUrl", blazor.DefaultEndpoint.Url);
        Output(this, "BlazorEndpointDomain", blazor.DefaultEndpoint.DomainName);
        Output(this, "BlazorEndpointArn", blazor.DefaultEndpoint.EndpointArn);
        Output(this, "BlazorDistributionDomain", blazorDistribution.DistributionDomainName);
        Output(this, "BlazorDistributionId", blazorDistribution.DistributionId);

        Output(this, "CallerRoleArn", callerRole.RoleArn);
        Output(this, "DeniedRoleArn", deniedRole.RoleArn);
        Output(this, "ImportedCallerImmutableRoleArn", naming.ImportedCallerImmutableRoleArn);
        Output(this, "ImportedCallerMutableRoleArn", naming.ImportedCallerMutableRoleArn);
    }

    /// <summary>
    /// Builds a CloudFront distribution fronting one IAM-authenticated endpoint via origin access control. The
    /// default behavior is uncached so dynamic responses stay live; the supplied path patterns are cached.
    /// </summary>
    private static Distribution CreateDistribution(
        Construct scope,
        string id,
        IWebFunctionEndpoint endpoint,
        string comment,
        params string[] cachedPathPatterns)
    {
        // Bound once; the same origin instance is referenced by the default behavior and every cached behavior.
        var origin = WebFunctionOrigin.WithOriginAccessControl(endpoint);

        var additionalBehaviors = new Dictionary<string, IBehaviorOptions>();
        foreach (var pattern in cachedPathPatterns)
        {
            additionalBehaviors[pattern] = new BehaviorOptions
            {
                Origin = origin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
            };
        }

        return new Distribution(scope, id, new DistributionProps
        {
            Comment = comment,
            PriceClass = PriceClass.PRICE_CLASS_100,
            DefaultBehavior = new BehaviorOptions
            {
                Origin = origin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
            },
            AdditionalBehaviors = additionalBehaviors,
        });
    }

    private static void Output(Construct scope, string id, string value)
        => _ = new CfnOutput(scope, id, new CfnOutputProps { Value = value });
}
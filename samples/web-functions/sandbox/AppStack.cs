using System.Collections.Generic;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using LogGroup = Amazon.CDK.AWS.Logs.LogGroup;
using LogGroupProps = Amazon.CDK.AWS.Logs.LogGroupProps;
using RetentionDays = Amazon.CDK.AWS.Logs.RetentionDays;
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
        // Non-default retention on *both* managed groups (30-day centralized, 1-day companion) proves the
        // independent retention wiring in Create mode is not accidentally pinned to the 14/3 defaults.
        var blazor = new WebFunctionConstruct(this, "Blazor", new WebFunctionConstructProps
        {
            FunctionName = naming.BlazorFunctionName,
            Code = WebFunctionCode.FromBucket(externalBucket, configuration.Blazor.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
                ObjectVersion = configuration.Blazor.ObjectVersion,
            }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Logs = WebFunctionLogs.Create(RetentionDays.ONE_MONTH, RemovalPolicy.DESTROY, RetentionDays.ONE_DAY),
            Description = "Sandbox Blazor Static SSR Web Function (non-default 30d/1d log retention)",
        });

        var blazorDistribution = CreateDistribution(
            this,
            "BlazorDistribution",
            blazor.DefaultEndpoint,
            "Sandbox Blazor distribution (dynamic document, immutable framework assets cached)",
            "/_framework/*",
            "/_content/*");

        // --- Use-mode logging workloads (§19.1 3.9). ---
        // UseLogs: a caller-owned centralized group whose name lives outside /aws/lambda/* plus a supplied
        // mutable execution role. No companion is supplied, so Lambda creates the .local companion on first
        // request - exercising G1 and the previously-unverified G2 (logs:CreateLogGroup on the companion's
        // exact ARN, without the log-stream suffix).
        var useLogsCentralGroup = new LogGroup(this, "UseLogsCentralGroup", new LogGroupProps
        {
            LogGroupName = naming.UseLogsCentralizedGroupName,
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });
        var useLogsRole = new Role(this, "UseLogsRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Supplied execution role for the Use-mode logs workload (identity-policy grants).",
        });
        useLogsRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"));
        var useLogs = new WebFunctionConstruct(this, "UseLogs", new WebFunctionConstructProps
        {
            FunctionName = naming.UseLogsFunctionName,
            Code = WebFunctionCode.FromBucket(artifactsBucket, configuration.NextJs.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                ObjectVersion = configuration.NextJs.ObjectVersion,
            }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Role = useLogsRole,
            Logs = WebFunctionLogs.Use(useLogsCentralGroup),
            Description = "Use-mode Web Function without a supplied companion (Lambda creates it)",
        });

        // UseCompanion: both the centralized group and the .local companion are caller-owned with custom
        // retention; proves the construct does not create a duplicate and continues to log.
        var useCompanionCentralGroup = new LogGroup(this, "UseCompanionCentralGroup", new LogGroupProps
        {
            LogGroupName = naming.UseCompanionCentralizedGroupName,
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });
        var useCompanionGroup = new LogGroup(this, "UseCompanionGroup", new LogGroupProps
        {
            LogGroupName = naming.UseCompanionCompanionGroupName,
            Retention = RetentionDays.THREE_DAYS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });
        var useCompanionRole = new Role(this, "UseCompanionRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Supplied execution role for the Use-mode companion workload (identity-policy grants).",
        });
        useCompanionRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"));
        var useCompanion = new WebFunctionConstruct(this, "UseCompanion", new WebFunctionConstructProps
        {
            FunctionName = naming.UseCompanionFunctionName,
            Code = WebFunctionCode.FromBucket(artifactsBucket, configuration.NextJs.ObjectKey, new WebFunctionCodeFromBucketOptions
            {
                ObjectVersion = configuration.NextJs.ObjectVersion,
            }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Role = useCompanionRole,
            Logs = WebFunctionLogs.Use(useCompanionCentralGroup, useCompanionGroup),
            Description = "Use-mode Web Function with a caller-supplied companion group",
        });

        // --- Operator roles. ---
        var callerRole = new Role(this, "CallerRole", new RoleProps
        {
            AssumedBy = new AccountPrincipal(configuration.Account),
            Description = "Operator role granted invoke on the ASP.NET Core default endpoint (identity policy).",
        });
        aspNetCore.DefaultEndpoint.GrantInvoke(callerRole);

        // Use-mode members: reachable by the same granted caller, so smoke.sh can drive real traffic that
        // forces Lambda to create the companion group for the UseLogs workload.
        useLogs.DefaultEndpoint.GrantInvoke(callerRole);
        useCompanion.DefaultEndpoint.GrantInvoke(callerRole);

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

        Output(this, "UseLogsFunctionName", useLogs.FunctionName);
        Output(this, "UseLogsEndpointUrl", useLogs.DefaultEndpoint.Url);
        Output(this, "UseLogsEndpointArn", useLogs.DefaultEndpoint.EndpointArn);
        Output(this, "UseCompanionFunctionName", useCompanion.FunctionName);
        Output(this, "UseCompanionEndpointUrl", useCompanion.DefaultEndpoint.Url);
        Output(this, "UseCompanionEndpointArn", useCompanion.DefaultEndpoint.EndpointArn);

        // Single source of truth for the log-group verification tooling (verify-log-groups.sh): the exact
        // centralized/companion names and expected retention for every sandbox function, plus whether the
        // companion is CloudFormation-owned (false for the Use-mode group Lambda creates on first request).
        Output(this, "LogGroupExpectations", BuildLogGroupExpectations(naming));
    }

    /// <summary>Describes the expected log groups for every sandbox function, as a JSON array.</summary>
    private static string BuildLogGroupExpectations(SandboxNaming naming)
    {
        var expectations = new List<LogGroupExpectation>
        {
            Managed(naming.NextJsFunctionName, $"/aws/lambda/web/{naming.NextJsFunctionName}", 14, 3),
            Managed(naming.AspNetCoreFunctionName, $"/aws/lambda/web/{naming.AspNetCoreFunctionName}", 14, 3),
            Managed(naming.BlazorFunctionName, $"/aws/lambda/web/{naming.BlazorFunctionName}", 30, 1),
            new(naming.UseLogsFunctionName, naming.UseLogsCentralizedGroupName, 7, naming.UseLogsCompanionGroupName, null, false),
            Managed(naming.UseCompanionFunctionName, naming.UseCompanionCentralizedGroupName, 14, 3),
        };

        return JsonSerializer.Serialize(expectations);
    }

    private static LogGroupExpectation Managed(string function, string centralized, int centralizedDays, int companionDays)
        => new(function, centralized, centralizedDays, $"{centralized}.local", companionDays, true);

    private sealed record LogGroupExpectation(
        string Function,
        string Centralized,
        int? CentralizedRetentionDays,
        string Companion,
        int? CompanionRetentionDays,
        bool CompanionManaged);

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
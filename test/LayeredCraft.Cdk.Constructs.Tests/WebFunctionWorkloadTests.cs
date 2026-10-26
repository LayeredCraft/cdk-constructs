using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.Assertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Workload compatibility synthesis tests (design §15, §19.1 item 2). Every workload is built through the same
/// public API; the only differences are the code artifact, the entry point and the environment variables, plus
/// the consumer's CloudFront behaviors. There is no workload-specific construct, helper or packaging logic in
/// the library. These tests only prove the workloads wire up and synthesize; runtime behavior is covered by the
/// deployed smoke tests in T7c, not here.
/// </summary>
[Collection("CDK Tests")]
public class WebFunctionWorkloadTests
{
    private static WebFunctionConstruct CreateWorkload(
        Stack stack,
        string id,
        string functionName,
        string codeKey,
        string entryPoint,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        var artifacts = Bucket.FromBucketName(stack, id + "Artifacts", "workload-artifacts");

        return new WebFunctionConstruct(stack, id, new WebFunctionConstructProps
        {
            FunctionName = functionName,
            Code = WebFunctionCode.FromBucket(
                artifacts,
                codeKey,
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            EntryPoint = entryPoint,
            EnvironmentVariables = environmentVariables,
        });
    }

    private static void ShouldHaveEntryPoint(Template template, string functionName, string entryPoint)
    {
        template.HasResourceProperties("AWS::Lambda::WebFunctionRevision", Match.ObjectLike(new Dictionary<string, object>
        {
            ["FunctionName"] = functionName,
            ["ServiceConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["EnvironmentVariables"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["AWS_LAMBDA_NODEJS_ENTRYPOINT"] = entryPoint,
                }),
            }),
        }));
    }

    [Fact]
    public void NextJs_ShouldSynthesizeThroughPublicApi()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Next.js standalone output: the zip root carries an index.js shim that sets HOSTNAME/PORT from
        // AWS_LAMBDA_HTTP_ENDPOINT and then requires ./server.js (design §16 workload recipe 1).
        var function = CreateWorkload(stack, "nextjs", "acme-nextjs", "artifacts/web.zip", "index.js", new Dictionary<string, string>
        {
            ["NODE_ENV"] = "production",
            ["NEXT_TELEMETRY_DISABLED"] = "1",
        });

        var origin = WebFunctionOrigin.WithOriginAccessControl(function.DefaultEndpoint, new WebFunctionOriginWithOacProps
        {
            CustomHeaders = new Dictionary<string, string> { ["x-origin-verify"] = "nextjs-secret" },
        });
        _ = new Distribution(stack, "nextjsDistribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = origin,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/_next/static/*"] = new BehaviorOptions { Origin = origin, CachePolicy = CachePolicy.CACHING_OPTIMIZED },
            },
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-nextjs");
        template.ShouldHaveRevision("acme-nextjs");
        template.ShouldHaveEndpoint("acme-nextjs", "default", WebFunctionAuthType.IamAuth);
        ShouldHaveEntryPoint(template, "acme-nextjs", "index.js");
        template.ShouldHaveWebFunctionOac("sigv4");
        template.ShouldAllowCloudFront("acme-nextjs", "default");
    }

    [Fact]
    public void AspNetCoreExecveShim_ShouldSynthesizeThroughPublicApi()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Self-contained linux-arm64 publish plus an index.js shim that process.execve("/var/task/server", ...)
        // and POSTs readiness to the runtime API (design §16 workload recipe 2). The shim is index.js at the
        // zip root, so no entry point override is required; it is set here only to make the contract explicit.
        _ = CreateWorkload(stack, "aspnet", "acme-aspnet", "artifacts/api.zip", "index.js", new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-aspnet");
        template.ShouldHaveRevision("acme-aspnet");
        template.ShouldHaveEndpoint("acme-aspnet", "default", WebFunctionAuthType.IamAuth);
        ShouldHaveEntryPoint(template, "acme-aspnet", "index.js");
    }

    [Fact]
    public void BlazorStaticSsrShim_ShouldSynthesizeThroughPublicApi()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Blazor Static SSR uses the same Node.js execve shim and readiness contract as ASP.NET Core
        // (design §16 workload recipe 3); the distinguishing artifact is the untrimmed SSR publish that also
        // carries wwwroot/_framework, so the code key differs.
        _ = CreateWorkload(stack, "blazor", "acme-blazor", "artifacts/blazor.zip", "index.js", new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-blazor");
        template.ShouldHaveRevision("acme-blazor");
        template.ShouldHaveEndpoint("acme-blazor", "default", WebFunctionAuthType.IamAuth);
        ShouldHaveEntryPoint(template, "acme-blazor", "index.js");
    }

    [Fact]
    public void AllWorkloads_ShouldUseOnlyTheSamePublicConstruct()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = CreateWorkload(stack, "nextjs", "acme-nextjs", "artifacts/web.zip", "index.js",
            new Dictionary<string, string> { ["NODE_ENV"] = "production" });
        _ = CreateWorkload(stack, "aspnet", "acme-aspnet", "artifacts/api.zip", "index.js",
            new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production" });
        _ = CreateWorkload(stack, "blazor", "acme-blazor", "artifacts/blazor.zip", "index.js",
            new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production" });

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::Lambda::WebFunction", 3);
        template.ResourceCountIs("AWS::Lambda::WebFunctionRevision", 3);
        template.ResourceCountIs("AWS::Lambda::WebFunctionEndpoint", 3);
    }
}

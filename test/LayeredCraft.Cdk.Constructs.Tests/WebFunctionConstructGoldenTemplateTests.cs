using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Golden;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Golden-template compatibility tests for <see cref="WebFunctionConstruct"/> (design §12.4, §15). Each case pins
/// the complete synthesized CloudFormation template for a representative configuration.
/// </summary>
[Collection("CDK Tests")]
public class WebFunctionConstructGoldenTemplateTests
{
    [Fact]
    public void WebFunction_Defaults_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-web-function");

        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "golden-artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = "golden-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        GoldenTemplate.AssertMatches(stack, "web-function-default.json");
    }

    [Fact]
    public void WebFunction_ApplicationManaged_WithServiceDefaultLogs_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-web-function");

        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "golden-artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = "golden-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.ApplicationManaged },
            Logs = WebFunctionLogs.ServiceDefault(),
        });

        GoldenTemplate.AssertMatches(stack, "web-function-application-managed.json");
    }

    [Fact]
    public void WebFunction_UseLogGroup_WithSuppliedRole_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-web-function");

        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "golden-artifacts");

        var logGroup = new LogGroup(stack, "ExistingLogGroup", new LogGroupProps
        {
            LogGroupName = "/custom/web/logs",
        });

        var role = new Role(stack, "SuppliedRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });

        _ = new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = "golden-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions
            {
                EndpointName = "default",
                AuthType = WebFunctionAuthType.IamAuth,
            },
            EnvironmentVariables = new Dictionary<string, string> { ["TABLE_NAME"] = "golden-table" },
            Logs = WebFunctionLogs.Use(logGroup),
            Role = role,
            ApplicationLogLevel = ApplicationLogLevel.INFO,
            SystemLogLevel = SystemLogLevel.WARN,
        });

        GoldenTemplate.AssertMatches(stack, "web-function-use-log-group.json");
    }

    [Fact]
    public void WebFunction_OwnedBucket_WithAutomaticPolicy_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-web-function");

        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });

        _ = new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = "golden-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app-v2.zip",
                new WebFunctionCodeFromBucketOptions { ObjectVersion = "version-1" }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        GoldenTemplate.AssertMatches(stack, "web-function-owned-bucket.json");
    }

    [Fact]
    public void WebFunction_ExternallyManagedBucket_WithMultiRegionEndpoint_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-web-function");

        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "golden-artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = "golden-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions
            {
                EndpointName = "public",
                AuthType = WebFunctionAuthType.IamAuth,
                EndpointType = WebFunctionEndpointType.MultiRegion,
                Regions = ["us-west-2"],
                MaxEnvironments = 10,
                RateLimit = 500,
                Description = "Public endpoint",
            },
        });

        GoldenTemplate.AssertMatches(stack, "web-function-externally-managed.json");
    }
}

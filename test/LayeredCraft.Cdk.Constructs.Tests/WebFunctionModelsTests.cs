using Amazon.CDK;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionModelsTests
{
    // -------------------------------------------------------------------------
    // Runtime
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionRuntime_NodeJs24X_HasExpectedName() =>
        WebFunctionRuntime.NodeJs24X.Name.Should().Be("nodejs24.x");

    [Fact]
    public void WebFunctionRuntime_Of_SetsName()
    {
        var runtime = WebFunctionRuntime.Of("nodejs22.x");

        runtime.Name.Should().Be("nodejs22.x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WebFunctionRuntime_Of_RejectsEmptyName(string? name)
    {
        Action act = () => WebFunctionRuntime.Of(name!);

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // Code
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionCode_FromBucket_ReturnsCodeWithValues()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal("web-function-code");
        var bucket = Bucket.FromBucketName(stack, "Artifacts", "test-artifacts");

        var code = WebFunctionCode.FromBucket(bucket, "artifacts/app.zip");

        var fromBucket = code.Should().BeOfType<WebFunctionCodeFromBucket>().Subject;
        fromBucket.Bucket.Should().BeSameAs(bucket);
        fromBucket.Key.Should().Be("artifacts/app.zip");
        fromBucket.Options.ObjectVersion.Should().BeNull();
        fromBucket.Options.BucketPolicy.Should().Be(WebFunctionCodeBucketPolicy.Automatic);
    }

    [Fact]
    public void WebFunctionCode_FromBucket_AppliesOptions()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal("web-function-code-options");
        var bucket = Bucket.FromBucketName(stack, "Artifacts", "test-artifacts");

        var code = WebFunctionCode.FromBucket(
            bucket,
            "artifacts/app.zip",
            new WebFunctionCodeFromBucketOptions
            {
                ObjectVersion = "v1",
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
            });

        var fromBucket = code.Should().BeOfType<WebFunctionCodeFromBucket>().Subject;
        fromBucket.Options.ObjectVersion.Should().Be("v1");
        fromBucket.Options.BucketPolicy.Should().Be(WebFunctionCodeBucketPolicy.ExternallyManaged);
    }

    [Fact]
    public void WebFunctionCode_FromBucket_RejectsNullBucket()
    {
        Action act = () => WebFunctionCode.FromBucket(null!, "artifacts/app.zip");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WebFunctionCode_FromBucket_RejectsEmptyKey(string? key)
    {
        var stack = CdkTestHelper.CreateTestStackMinimal("web-function-code-key");
        var bucket = Bucket.FromBucketName(stack, "Artifacts", "test-artifacts");

        Action act = () => WebFunctionCode.FromBucket(bucket, key!);

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // Logs
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionLogs_Create_DefaultsToTwoWeeksAndDestroy()
    {
        var logs = WebFunctionLogs.Create().Should().BeOfType<WebFunctionLogsCreate>().Subject;

        logs.Retention.Should().Be(RetentionDays.TWO_WEEKS);
        logs.RemovalPolicy.Should().Be(RemovalPolicy.DESTROY);
    }

    [Fact]
    public void WebFunctionLogs_Create_AppliesOverrides()
    {
        var logs = WebFunctionLogs
            .Create(RetentionDays.ONE_MONTH, RemovalPolicy.RETAIN)
            .Should().BeOfType<WebFunctionLogsCreate>().Subject;

        logs.Retention.Should().Be(RetentionDays.ONE_MONTH);
        logs.RemovalPolicy.Should().Be(RemovalPolicy.RETAIN);
    }

    [Fact]
    public void WebFunctionLogs_ServiceDefault_IsServiceDefault()
    {
        WebFunctionLogs.ServiceDefault().Should().BeOfType<WebFunctionLogsServiceDefault>();
    }

    [Fact]
    public void WebFunctionLogs_Use_CarriesLogGroup()
    {
        var logGroup = new LogGroup(
            CdkTestHelper.CreateTestStackMinimal("web-function-logs-use"),
            "LogGroup",
            new LogGroupProps { LogGroupName = "/custom/group" });

        var logs = WebFunctionLogs.Use(logGroup).Should().BeOfType<WebFunctionLogsUse>().Subject;

        logs.LogGroup.Should().BeSameAs(logGroup);
    }

    [Fact]
    public void WebFunctionLogs_Use_RejectsNull()
    {
        Action act = () => WebFunctionLogs.Use(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Construct props
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionConstructProps_HasExpectedDefaults()
    {
        var props = CreateValidProps();

        props.FunctionName.Should().Be("test-web-function");
        props.Runtime.Should().BeSameAs(WebFunctionRuntime.NodeJs24X);
        props.Timeout.ToSeconds().Should().Be(30);
        props.EntryPoint.Should().BeNull();
        props.EnvironmentVariables.Should().BeEmpty();
        props.MaxConcurrencyPerEnvironment.Should().BeNull();
        props.Description.Should().BeNull();
        props.Role.Should().BeNull();
        props.ApplicationLogLevel.Should().BeNull();
        props.SystemLogLevel.Should().BeNull();
        props.Logs.Should().BeOfType<WebFunctionLogsCreate>();
    }

    // -------------------------------------------------------------------------
    // Endpoint options / attributes
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionEndpointOptions_HasExpectedDefaults()
    {
        var options = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth };

        options.EndpointName.Should().Be("default");
        options.EndpointType.Should().Be(WebFunctionEndpointType.HomeRegion);
        options.Regions.Should().BeEmpty();
        options.MaxEnvironments.Should().BeNull();
        options.RateLimit.Should().BeNull();
        options.Description.Should().BeNull();
    }

    [Fact]
    public void WebFunctionEndpointOptions_IsInitOnly()
    {
        var original = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth };
        var copy = original with { EndpointName = "secondary" };

        original.EndpointName.Should().Be("default");
        copy.EndpointName.Should().Be("secondary");
    }

    [Fact]
    public void WebFunctionEndpointAttributes_DefaultsEndpointTypeToHomeRegion()
    {
        var attributes = new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:123456789012:web-function:test",
            DomainName = "example.lambda-url.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        };

        attributes.EndpointType.Should().Be(WebFunctionEndpointType.HomeRegion);
    }

    // -------------------------------------------------------------------------
    // Invoke grant options
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionInvokeGrantOptions_DefaultsCallerPolicyToAutomatic()
    {
        new WebFunctionInvokeGrantOptions().CallerPolicy.Should().Be(WebFunctionCallerPolicy.Automatic);
    }

    // -------------------------------------------------------------------------
    // Origin props
    // -------------------------------------------------------------------------

    [Fact]
    public void WebFunctionOriginProps_DefaultsToNullMembers()
    {
        var props = new WebFunctionOriginProps();

        props.OriginId.Should().BeNull();
        props.OriginPath.Should().BeNull();
        props.CustomHeaders.Should().BeNull();
        props.ConnectionAttempts.Should().BeNull();
        props.ConnectionTimeout.Should().BeNull();
        props.ReadTimeout.Should().BeNull();
        props.KeepaliveTimeout.Should().BeNull();
        props.ResponseCompletionTimeout.Should().BeNull();
        props.OriginShieldEnabled.Should().BeNull();
        props.OriginShieldRegion.Should().BeNull();
    }

    [Fact]
    public void WebFunctionOriginWithOacProps_DefaultsPermissionsToAutomatic()
    {
        var props = new WebFunctionOriginWithOacProps();

        props.Permissions.Should().Be(WebFunctionOriginPermissions.Automatic);
        props.OriginAccessControl.Should().BeNull();
    }

    private static WebFunctionConstructProps CreateValidProps()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal("web-function-props");
        var bucket = Bucket.FromBucketName(stack, "Artifacts", "test-artifacts");

        return new WebFunctionConstructProps
        {
            FunctionName = "test-web-function",
            Code = WebFunctionCode.FromBucket(bucket, "artifacts/app.zip"),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        };
    }
}

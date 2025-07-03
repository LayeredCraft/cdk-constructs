using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Constructs;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Extensions;

namespace LayeredCraft.Cdk.Constructs.Tests.Testing;

[Collection("CDK Tests")]
public class TestingHelpersTests
{
    [Fact]
    public void CdkTestHelper_ShouldCreateValidTestStack()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal();

        stack.Should().NotBeNull();
    }

    [Fact]
    public void PropsBuilder_ShouldCreateValidProps()
    {
        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("test-func")
            .WithDynamoDbAccess("test-table")
            .WithEnvironmentVariable("TEST_VAR", "test-value")
            .Build();

        props.FunctionName.Should().Be("test-func");
        props.PolicyStatements.Should().HaveCount(1);
        props.EnvironmentVariables.Should().ContainKey("TEST_VAR");
        props.EnvironmentVariables["TEST_VAR"].Should().Be("test-value");
    }

    [Fact]
    public void AssertionHelpers_ShouldWorkWithActualConstruct()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal();

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("helper-test")
            .WithFunctionSuffix("validation")
            .WithOtelEnabled(true)
            .Build();

        _ = new LambdaFunctionConstruct(stack, "TestConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        // Test our assertion helpers work correctly
        template.ShouldHaveLambdaFunction("helper-test-validation");
        template.ShouldHaveOtelLayer();
        template.ShouldHaveCloudWatchLogsPermissions("helper-test-validation");
        template.ShouldHaveVersionAndAlias("live");
        template.ShouldHaveLogGroup("helper-test-validation", 14);
    }

    [Fact]
    public void AssertionHelpers_ShouldDetectMissingOtel()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal();

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithOtelEnabled(false)
            .Build();

        _ = new LambdaFunctionConstruct(stack, "TestConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        template.ShouldNotHaveOtelLayer();
    }

    [Fact]
    public void AssertionHelpers_ShouldVerifyEnvironmentVariables()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal();

        var expectedVars = new Dictionary<string, string>
        {
            { "API_URL", "https://api.example.com" },
            { "LOG_LEVEL", "debug" },
            { "FEATURE_FLAG", "enabled" }
        };

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("env-test")
            .WithEnvironmentVariables(expectedVars)
            .Build();

        _ = new LambdaFunctionConstruct(stack, "TestConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        template.ShouldHaveEnvironmentVariables(expectedVars);
    }

    [Fact]
    public void AssertionHelpers_ShouldVerifyLambdaPermissions()
    {
        var stack = CdkTestHelper.CreateTestStackMinimal();

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("permission-test")
            .WithApiGatewayPermission("arn:aws:execute-api:us-east-1:123456789012:abcdef123/prod/GET/test")
            .WithAlexaPermission("amzn1.ask.skill.test-skill-id")
            .Build();

        _ = new LambdaFunctionConstruct(stack, "TestConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        // Should have 2 permissions * 3 targets (function + version + alias) = 6 total resources
        template.ShouldHaveLambdaPermissions(2);
    }

    [Fact]
    public void PropsBuilder_ShouldConfigureS3Access()
    {
        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("s3-test")
            .WithS3Access("my-test-bucket")
            .Build();

        props.FunctionName.Should().Be("s3-test");
        props.PolicyStatements.Should().HaveCount(1);
        
        var s3Statement = props.PolicyStatements[0];
        s3Statement.Actions.Should().Contain("s3:GetObject");
        s3Statement.Actions.Should().Contain("s3:PutObject");
        s3Statement.Actions.Should().Contain("s3:DeleteObject");
        s3Statement.Resources.Should().Contain("arn:aws:s3:::my-test-bucket/*");
    }

    [Fact]
    public void PropsBuilder_ShouldAddCustomPolicy()
    {
        var customStatement = new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
        {
            Actions = new[] { "sns:Publish" },
            Resources = new[] { "arn:aws:sns:us-east-1:123456789012:my-topic" },
            Effect = Amazon.CDK.AWS.IAM.Effect.ALLOW
        });

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("custom-policy-test")
            .WithCustomPolicy(customStatement)
            .Build();

        props.FunctionName.Should().Be("custom-policy-test");
        props.PolicyStatements.Should().HaveCount(1);
        
        var snsStatement = props.PolicyStatements[0];
        snsStatement.Actions.Should().Contain("sns:Publish");
        snsStatement.Resources.Should().Contain("arn:aws:sns:us-east-1:123456789012:my-topic");
    }

    [Fact]
    public void PropsBuilder_ShouldAddCustomPermission()
    {
        var customPermission = new LayeredCraft.Cdk.Constructs.Models.LambdaPermission
        {
            Principal = "events.amazonaws.com",
            Action = "lambda:InvokeFunction",
            SourceArn = "arn:aws:events:us-east-1:123456789012:rule/my-rule"
        };

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("custom-permission-test")
            .WithCustomPermission(customPermission)
            .Build();

        props.FunctionName.Should().Be("custom-permission-test");
        props.Permissions.Should().HaveCount(1);
        
        var permission = props.Permissions[0];
        permission.Principal.Should().Be("events.amazonaws.com");
        permission.Action.Should().Be("lambda:InvokeFunction");
        permission.SourceArn.Should().Be("arn:aws:events:us-east-1:123456789012:rule/my-rule");
    }

    [Fact]
    public void CdkTestHelper_ShouldResolveTestAssetPath()
    {
        var assetPath = CdkTestHelper.GetTestAssetPath("TestAssets/custom-file.zip");
        
        assetPath.Should().NotBeNullOrEmpty();
        assetPath.Should().EndWith("TestAssets/custom-file.zip");
        Path.IsPathRooted(assetPath).Should().BeTrue("Should return absolute path");
    }

    [Fact]
    public void CdkTestHelper_ShouldReturnStandardLambdaZipPath()
    {
        var zipPath = CdkTestHelper.GetTestLambdaZipPath();
        
        zipPath.Should().NotBeNullOrEmpty();
        zipPath.Should().EndWith("TestAssets/test-lambda.zip");
        Path.IsPathRooted(zipPath).Should().BeTrue("Should return absolute path");
    }
}
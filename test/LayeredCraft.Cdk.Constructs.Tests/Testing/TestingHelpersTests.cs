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
            Actions = ["sns:Publish"],
            Resources = ["arn:aws:sns:us-east-1:123456789012:my-topic"],
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
        var customPermission = new Models.LambdaPermission
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

    [Fact]
    public void CdkTestHelper_ShouldCreateStackWithCustomStackProps()
    {
        var customProps = new Amazon.CDK.StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "987654321098", 
                Region = "eu-west-1" 
            },
            Tags = new Dictionary<string, string>
            {
                { "Environment", "test" },
                { "Project", "custom-test" }
            }
        };

        var (app, stack) = CdkTestHelper.CreateTestStack("custom-test-stack", customProps);

        app.Should().NotBeNull();
        stack.Should().NotBeNull();
        stack.StackName.Should().Be("custom-test-stack");
        stack.Account.Should().Be("987654321098");
        stack.Region.Should().Be("eu-west-1");
    }

    [Fact]
    public void CdkTestHelper_ShouldCreateMinimalStackWithCustomStackProps()
    {
        var customProps = new Amazon.CDK.StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "555666777888", 
                Region = "ap-southeast-2" 
            },
            Description = "Test stack with custom props"
        };

        var stack = CdkTestHelper.CreateTestStackMinimal("minimal-custom-stack", customProps);

        stack.Should().NotBeNull();
        stack.StackName.Should().Be("minimal-custom-stack");
        stack.Account.Should().Be("555666777888");
        stack.Region.Should().Be("ap-southeast-2");
    }

    [Fact]
    public void CdkTestHelper_ShouldWorkWithRealWorldCustomStackProps()
    {
        // Simulate a real-world scenario similar to LightsaberStackProps
        var customEnv = new Amazon.CDK.Environment 
        { 
            Account = "123456789012", 
            Region = "us-west-2" 
        };

        var customProps = new Amazon.CDK.StackProps
        {
            Env = customEnv,
            Tags = new Dictionary<string, string>
            {
                { "Application", "MyApp" },
                { "Environment", "test" },
                { "Owner", "DevTeam" }
            },
            Description = "Test infrastructure stack"
        };

        var stack = CdkTestHelper.CreateTestStackMinimal("real-world-test", customProps);

        stack.Should().NotBeNull();
        stack.StackName.Should().Be("real-world-test");
        stack.Account.Should().Be("123456789012");
        stack.Region.Should().Be("us-west-2");

        // Verify we can create constructs on this stack
        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("real-world-function")
            .Build();

        _ = new LambdaFunctionConstruct(stack, "RealWorldConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        template.ShouldHaveLambdaFunction("real-world-function-test");
    }

    [Fact]
    public void CdkTestHelper_ShouldCreateGenericStackType()
    {
        var customProps = new Amazon.CDK.StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "111222333444", 
                Region = "ca-central-1" 
            },
            Description = "Generic stack test"
        };

        // Create a custom stack type using the generic method
        var (app, stack) = CdkTestHelper.CreateTestStack<TestCustomStack>("generic-test-stack", customProps);

        app.Should().NotBeNull();
        stack.Should().NotBeNull();
        stack.Should().BeOfType<TestCustomStack>();
        stack.StackName.Should().Be("generic-test-stack");
        stack.Account.Should().Be("111222333444");
        stack.Region.Should().Be("ca-central-1");
        
        // Verify custom stack functionality
        var customStack = stack;
        customStack.CustomProperty.Should().Be("CustomValue");
    }

    [Fact]
    public void CdkTestHelper_ShouldCreateGenericStackTypeMinimal()
    {
        var customProps = new Amazon.CDK.StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "555777999111", 
                Region = "eu-central-1" 
            },
            Tags = new Dictionary<string, string>
            {
                { "TestType", "Generic" },
                { "Framework", "CDK" }
            }
        };

        // Create custom stack type using the minimal generic method
        var stack = CdkTestHelper.CreateTestStackMinimal<TestCustomStack>("minimal-generic-stack", customProps);

        stack.Should().NotBeNull();
        stack.Should().BeOfType<TestCustomStack>();
        stack.StackName.Should().Be("minimal-generic-stack");
        stack.Account.Should().Be("555777999111");
        stack.Region.Should().Be("eu-central-1");
        
        // Verify custom stack functionality
        var customStack = stack;
        customStack.CustomProperty.Should().Be("CustomValue");
    }

    [Fact]
    public void CdkTestHelper_ShouldWorkWithGenericStackAndConstructs()
    {
        var customProps = new Amazon.CDK.StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "888999000111", 
                Region = "us-east-2" 
            }
        };

        // Create custom stack and add constructs to it
        var stack = CdkTestHelper.CreateTestStackMinimal<TestCustomStack>("integration-test", customProps);

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("generic-function")
            .WithEnvironmentVariable("STACK_TYPE", "CustomStack")
            .Build();

        _ = new LambdaFunctionConstruct(stack, "GenericConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        // Verify everything works together
        template.ShouldHaveLambdaFunction("generic-function-test");
        template.ShouldHaveEnvironmentVariables(new Dictionary<string, string>
        {
            { "ENVIRONMENT", "test" },
            { "STACK_TYPE", "CustomStack" }
        });
        
        // Verify custom stack properties
        var customStack = stack;
        customStack.CustomProperty.Should().Be("CustomValue");
    }

    [Fact]
    public void CdkTestHelper_ShouldCreateDualGenericStackType()
    {
        var customProps = new TestCustomStackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "222333444555", 
                Region = "eu-west-2" 
            },
            Description = "Dual generic stack test",
            CustomContext = "test-context",
            EnableFeature = true
        };

        // Create stack using dual generics for exact type matching
        var (app, stack) = CdkTestHelper.CreateTestStack<TestAdvancedStack, TestCustomStackProps>("dual-generic-stack", customProps);

        app.Should().NotBeNull();
        stack.Should().NotBeNull();
        stack.Should().BeOfType<TestAdvancedStack>();
        stack.StackName.Should().Be("dual-generic-stack");
        stack.Account.Should().Be("222333444555");
        stack.Region.Should().Be("eu-west-2");
        
        // Verify custom stack functionality with custom props
        stack.CustomContext.Should().Be("test-context");
        stack.FeatureEnabled.Should().Be(true);
        stack.ProcessedValue.Should().Be("test-context-processed");
    }

    [Fact]
    public void CdkTestHelper_ShouldCreateDualGenericStackTypeMinimal()
    {
        var customProps = new TestCustomStackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "666777888999", 
                Region = "ap-northeast-1" 
            },
            CustomContext = "minimal-context",
            EnableFeature = false,
            Tags = new Dictionary<string, string>
            {
                { "TestMethod", "DualGeneric" },
                { "PropsType", "Custom" }
            }
        };

        // Create stack using dual generics minimal method
        var stack = CdkTestHelper.CreateTestStackMinimal<TestAdvancedStack, TestCustomStackProps>("dual-minimal-stack", customProps);

        stack.Should().NotBeNull();
        stack.Should().BeOfType<TestAdvancedStack>();
        stack.StackName.Should().Be("dual-minimal-stack");
        stack.Account.Should().Be("666777888999");
        stack.Region.Should().Be("ap-northeast-1");
        
        // Verify custom props were passed correctly
        stack.CustomContext.Should().Be("minimal-context");
        stack.FeatureEnabled.Should().Be(false);
        stack.ProcessedValue.Should().Be("minimal-context-processed");
    }

    [Fact]
    public void CdkTestHelper_ShouldWorkWithDualGenericStackAndConstructs()
    {
        var customProps = new TestCustomStackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = "111222333444", 
                Region = "us-west-1" 
            },
            CustomContext = "integration-test",
            EnableFeature = true
        };

        // Create custom stack with dual generics and add constructs
        var stack = CdkTestHelper.CreateTestStackMinimal<TestAdvancedStack, TestCustomStackProps>("integration-dual", customProps);

        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("dual-generic-function")
            .WithEnvironmentVariable("STACK_CONTEXT", stack.CustomContext)
            .WithEnvironmentVariable("FEATURE_ENABLED", stack.FeatureEnabled.ToString())
            .Build();

        _ = new LambdaFunctionConstruct(stack, "DualGenericConstruct", props);

        // Create template AFTER adding constructs to the stack
        var template = Template.FromStack(stack);

        // Verify everything works together with custom props
        template.ShouldHaveLambdaFunction("dual-generic-function-test");
        template.ShouldHaveEnvironmentVariables(new Dictionary<string, string>
        {
            { "ENVIRONMENT", "test" },
            { "STACK_CONTEXT", "integration-test" },
            { "FEATURE_ENABLED", "True" }
        });
        
        // Verify custom stack properties from custom props
        stack.CustomContext.Should().Be("integration-test");
        stack.FeatureEnabled.Should().Be(true);
        stack.ProcessedValue.Should().Be("integration-test-processed");
    }
}

// Test helper interface for custom props testing
public interface ITestCustomStackProps : Amazon.CDK.IStackProps
{
    string CustomContext { get; }
    bool EnableFeature { get; }
}

// Test helper class for custom props testing
public class TestCustomStackProps : Amazon.CDK.StackProps, ITestCustomStackProps
{
    public string CustomContext { get; set; } = string.Empty;
    public bool EnableFeature { get; set; }
}

// Test helper class for generic stack testing
public class TestCustomStack : Amazon.CDK.Stack
{
    public string CustomProperty { get; }

    public TestCustomStack(Amazon.CDK.App scope, string id, Amazon.CDK.IStackProps props) : base(scope, id, props)
    {
        CustomProperty = "CustomValue";
    }
}

// Test helper class for dual generic testing with custom props
public class TestAdvancedStack : Amazon.CDK.Stack
{
    public string CustomContext { get; }
    public bool FeatureEnabled { get; }
    public string ProcessedValue { get; }

    public TestAdvancedStack(Amazon.CDK.App scope, string id, ITestCustomStackProps props) : base(scope, id, props)
    {
        CustomContext = props.CustomContext;
        FeatureEnabled = props.EnableFeature;
        ProcessedValue = $"{props.CustomContext}-processed";
    }
}
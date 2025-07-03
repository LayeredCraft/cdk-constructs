using System.Reflection;
using Amazon.CDK;
using Amazon.CDK.Assertions;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Utility class providing common CDK testing helpers to reduce boilerplate in unit tests.
/// </summary>
public static class CdkTestHelper
{
    /// <summary>
    /// Creates a test CDK app and stack with sensible defaults for testing.
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <param name="stackName">The name of the test stack (default: test-stack)</param>
    /// <param name="region">AWS region (default: us-east-1)</param>
    /// <param name="account">AWS account ID (default: 123456789012)</param>
    /// <returns>Tuple containing the app and stack for testing</returns>
    public static (App app, Stack stack) CreateTestStack(
        string stackName = "test-stack",
        string region = "us-east-1", 
        string account = "123456789012")
    {
        var app = new App();
        var stack = new Stack(app, stackName, new StackProps
        {
            Env = new Amazon.CDK.Environment 
            { 
                Account = account, 
                Region = region 
            }
        });
        
        return (app, stack);
    }

    /// <summary>
    /// Creates a test CDK app and stack with custom stack props.
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <param name="stackName">The name of the test stack</param>
    /// <param name="stackProps">Custom stack props implementing IStackProps</param>
    /// <returns>Tuple containing the app and stack for testing</returns>
    public static (App app, Stack stack) CreateTestStack(string stackName, IStackProps stackProps)
    {
        var app = new App();
        var stack = new Stack(app, stackName, stackProps);
        
        return (app, stack);
    }

    /// <summary>
    /// Creates a test CDK app and custom stack type with the specified props.
    /// This method uses reflection to instantiate the custom stack type.
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <typeparam name="TStack">The custom stack type that inherits from Stack</typeparam>
    /// <param name="stackName">The name of the test stack</param>
    /// <param name="stackProps">Custom stack props implementing IStackProps</param>
    /// <returns>Tuple containing the app and the custom stack instance</returns>
    public static (App app, TStack stack) CreateTestStack<TStack>(string stackName, IStackProps stackProps) 
        where TStack : Stack
    {
        var app = new App();
        var stack = (TStack)Activator.CreateInstance(typeof(TStack), app, stackName, stackProps)!;
        return (app, stack);
    }

    /// <summary>
    /// Creates a test CDK stack (app is created internally).
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <param name="stackName">The name of the test stack (default: test-stack)</param>
    /// <param name="region">AWS region (default: us-east-1)</param>
    /// <param name="account">AWS account ID (default: 123456789012)</param>
    /// <returns>The stack for testing</returns>
    public static Stack CreateTestStackMinimal(
        string stackName = "test-stack",
        string region = "us-east-1", 
        string account = "123456789012")
    {
        var (_, stack) = CreateTestStack(stackName, region, account);
        return stack;
    }

    /// <summary>
    /// Creates a test CDK stack with custom stack props (app is created internally).
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <param name="stackName">The name of the test stack</param>
    /// <param name="stackProps">Custom stack props implementing IStackProps</param>
    /// <returns>The stack for testing</returns>
    public static Stack CreateTestStackMinimal(string stackName, IStackProps stackProps)
    {
        var (_, stack) = CreateTestStack(stackName, stackProps);
        return stack;
    }

    /// <summary>
    /// Creates a test CDK custom stack type with the specified props (app is created internally).
    /// This method uses reflection to instantiate the custom stack type.
    /// Note: You must create the Template.FromStack(stack) after adding constructs to the stack.
    /// </summary>
    /// <typeparam name="TStack">The custom stack type that inherits from Stack</typeparam>
    /// <param name="stackName">The name of the test stack</param>
    /// <param name="stackProps">Custom stack props implementing IStackProps</param>
    /// <returns>The custom stack instance for testing</returns>
    public static TStack CreateTestStackMinimal<TStack>(string stackName, IStackProps stackProps)
        where TStack : Stack
    {
        var (_, stack) = CreateTestStack<TStack>(stackName, stackProps);
        return stack;
    }

    /// <summary>
    /// Gets the path to a test asset relative to the executing assembly location.
    /// This ensures test assets can be found regardless of the current working directory.
    /// </summary>
    /// <param name="relativePath">Relative path from the test assembly location (e.g., "TestAssets/test-lambda.zip")</param>
    /// <returns>Absolute path to the test asset</returns>
    public static string GetTestAssetPath(string relativePath)
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Could not determine assembly directory");
        return Path.Combine(assemblyDirectory, relativePath);
    }

    /// <summary>
    /// Gets the standard test Lambda deployment package path.
    /// This is a convenience method for the most commonly used test asset.
    /// </summary>
    /// <returns>Absolute path to the test Lambda zip file</returns>
    public static string GetTestLambdaZipPath()
    {
        return GetTestAssetPath("TestAssets/test-lambda.zip");
    }

    /// <summary>
    /// Creates a LambdaFunctionConstructPropsBuilder with sensible test defaults.
    /// Uses the executing assembly location to find test assets reliably.
    /// </summary>
    /// <param name="assetPath">Optional custom asset path. If not provided, uses TestAssets/test-lambda.zip</param>
    /// <returns>A configured builder for creating test props</returns>
    public static LambdaFunctionConstructPropsBuilder CreatePropsBuilder(string? assetPath = null)
    {
        var defaultAssetPath = assetPath ?? GetTestLambdaZipPath();
        
        return new LambdaFunctionConstructPropsBuilder()
            .WithFunctionName("test-function")
            .WithFunctionSuffix("test")
            .WithAssetPath(defaultAssetPath)
            .WithRoleName("test-function-role")
            .WithPolicyName("test-function-policy")
            .WithEnvironmentVariable("ENVIRONMENT", "test")
            .WithOtelEnabled(true);
    }
}
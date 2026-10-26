---
layout: default
title: Testing Guide
permalink: /testing/
---

# Testing Guide

LayeredCraft CDK Constructs includes comprehensive testing utilities to help you write robust tests for your CDK infrastructure. The testing helpers provide fluent assertion methods, builders for test data, and utilities for common testing scenarios.

## Overview

The testing framework consists of:

- **Test Helpers**: Utilities for creating test stacks and props
- **Assertion Methods**: Fluent assertions for CDK resources
- **Props Builders**: Fluent builders for construct properties
- **AutoFixture Integration**: Automatic test data generation with realistic values

## Getting Started

### Basic Test Structure

```csharp
[Collection("CDK Tests")]
public class MyConstructTests
{
    [Fact]
    public void Should_Create_Lambda_Function()
    {
        // Arrange
        var (app, stack) = CdkTestHelper.CreateTestStack("test-stack");
        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionName("my-function")
            .WithMemorySize(512)
            .Build();

        // Act
        var construct = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveLambdaFunction("my-function-test");
        template.ShouldHaveMemorySize(512);
    }
}
```

### Important Test Collection

All CDK construct tests **MUST** be decorated with `[Collection("CDK Tests")]` to ensure sequential execution:

```csharp
[Collection("CDK Tests")]
public class LambdaFunctionConstructTests
{
    // Your tests here
}
```

This prevents parallel execution which can cause CDK context conflicts.

## Test Helpers

### CdkTestHelper

The main utility class for creating test infrastructure:

```csharp
// Create test stack with app
var (app, stack) = CdkTestHelper.CreateTestStack("test-stack");

// Create minimal test stack (app created internally)
var stack = CdkTestHelper.CreateTestStackMinimal("test-stack");

// Create custom stack types
var (app, customStack) = CdkTestHelper.CreateTestStack<MyCustomStack>("test-stack", props);
var customStack = CdkTestHelper.CreateTestStackMinimal<MyCustomStack>("test-stack", props);

// Get test asset paths
var lambdaPath = CdkTestHelper.GetTestAssetPath("test-lambda.zip");
var sitePath = CdkTestHelper.GetTestAssetPath("static-site");
```

### Generic Stack Creation

For testing custom stack types:

```csharp
// Single generic (for IStackProps)
var (app, stack) = CdkTestHelper.CreateTestStack<MyStack>("stack-name", stackProps);

// Dual generic (for custom props interfaces)
var (app, stack) = CdkTestHelper.CreateTestStack<MyStack, IMyProps>("stack-name", customProps);
```

## Lambda Function Testing

### Props Builder

```csharp
var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
    .WithFunctionName("my-function")
    .WithFunctionSuffix("prod")
    .WithMemorySize(1024)
    .WithTimeoutInSeconds(30)
    .WithEnvironmentVariable("LOG_LEVEL", "info")
    .WithDynamoDbAccess("my-table")
    .WithS3Access("my-bucket")
    .WithApiGatewayPermission("arn:aws:execute-api:us-east-1:123456789012:abcdef123/*")
    .WithOtelEnabled(true)
    .WithOtelLayerVersion("0-117-0")
    .WithArchitecture("arm64")
    .WithGenerateUrl(true)
    .Build();

// SnapStart is only supported for Java runtimes; avoid enabling it with the default runtime.
```

### OpenTelemetry Configuration (v2.0+)

The props builder includes new methods for configuring OpenTelemetry settings:

```csharp
var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
    .WithOtelEnabled(true)                    // Enable OTEL layer (disabled by default in v2.0+)
    .WithOtelLayerVersion("0-117-0")         // Specify OTEL layer version
    .WithArchitecture("arm64")               // Specify architecture (default: amd64)
    .Build();
```

**Available OTEL methods:**
- `WithOtelEnabled(bool)` - Enable/disable OpenTelemetry layer
- `WithOtelLayerVersion(string)` - Specify OTEL layer version (e.g., "0-117-0")  
- `WithArchitecture(string)` - Specify Lambda architecture ("amd64" or "arm64")

### Assertion Methods

```csharp
// Basic assertions
template.ShouldHaveLambdaFunction("my-function-prod");
template.ShouldHaveMemorySize(1024);
template.ShouldHaveTimeout(30);

// Advanced features
template.ShouldHaveOtelLayer();
template.ShouldHaveFunctionUrl();
template.ShouldHaveFunctionUrlOutput("test-stack", "test-construct");

// IAM and permissions
template.ShouldHaveCloudWatchLogsPermissions("my-function-prod");
template.ShouldHaveLambdaPermissions(1); // Count of permission objects

// Environment and configuration
template.ShouldHaveEnvironmentVariables(new Dictionary<string, string> 
{ 
    { "LOG_LEVEL", "info" } 
});
template.ShouldHaveVersionAndAlias("live");
template.ShouldHaveLogGroup("my-function-prod", 14);
```

SnapStart-specific assertions (`template.ShouldHaveSnapStart()`) apply only when you are using a Java runtime; the default `PROVIDED_AL2023` runtime does not support SnapStart.

## Static Site Testing

### Props Builder

```csharp
var props = CdkTestHelper.CreateStaticSitePropsBuilder()
    .WithDomainName("example.com")
    .WithSiteSubDomain("www")
    .WithApiDomain("api.example.com")
    .WithAlternateDomains("example.org", "www.example.org")
    .WithAssetPath("./static-site")
    .Build();
```

### Assertion Methods

```csharp
// Complete site setup
template.ShouldHaveCompleteStaticSite("my-bucket", "example.com", true, 3);

// Individual components
template.ShouldHaveStaticWebsiteBucket("my-bucket");
template.ShouldHaveCloudFrontDistribution("www.example.com");
template.ShouldHaveSSLCertificate("www.example.com");
template.ShouldHaveRoute53Records("www.example.com");
template.ShouldHaveMultipleRoute53Records(3);

// API proxy
template.ShouldHaveApiProxyBehavior("api.example.com");
template.ShouldNotHaveApiProxyBehavior();
```

## DynamoDB Table Testing

### Props Builder

```csharp
var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
    .WithTableName("my-table")
    .WithPartitionKey("userId", AttributeType.STRING)
    .WithSortKey("createdAt", AttributeType.NUMBER)
    .WithGlobalSecondaryIndex("email-index", "email", AttributeType.STRING)
    .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
    .WithTimeToLiveAttribute("expiresAt")
    .Build();
```

### Assertion Methods

```csharp
// Table structure
template.ShouldHaveDynamoTable("my-table");
template.ShouldHavePartitionKey("userId", AttributeType.STRING);
template.ShouldHaveSortKey("createdAt", AttributeType.NUMBER);

// GSI
template.ShouldHaveGlobalSecondaryIndex("email-index", "email", AttributeType.STRING);

// Streams and TTL
template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
template.ShouldHaveTimeToLiveAttribute("expiresAt");

// CloudFormation outputs
template.ShouldHaveTableOutputs("test-stack", "test-construct");
```

## Cognito User Pool Testing

### Props Builder

```csharp
var props = new CognitoUserPoolConstructPropsBuilder()
    .WithUserPoolName("my-pool")
    .WithSelfSignUpEnabled(true)
    .WithCognitoDomain("my-pool-auth")
    .AddResourceServer("My API", "my-api",
        scopes: [new CognitoResourceServerScopeProps { Name = "read", Description = "Read access" }])
    .AddWebAppClient(
        name: "web-app",
        callbackUrls: ["https://example.com/callback"],
        logoutUrls: ["https://example.com"])
    .AddGroup("admin", description: "Administrators", precedence: 1)
    .Build();

// Convenience preset for a standard web application setup
var webAppProps = new CognitoUserPoolConstructPropsBuilder()
    .ForWebApplication(userPoolName: "my-pool", cognitoDomainPrefix: "my-pool-auth")
    .Build();
```

### Assertion Methods

```csharp
// Pool and domain
template.ShouldHaveUserPool("my-app-users");
template.ShouldHaveCognitoUserPoolDomain("my-pool-auth");

// App clients and resource servers
template.ShouldHaveUserPoolClient("web-app");
template.ShouldHaveResourceServer("my-api");

// Groups
template.ShouldHaveUserPoolGroup("admin");

// Managed Login branding
template.ShouldHaveManagedLoginBranding();
template.ShouldNotHaveManagedLoginBranding();

// CloudFormation exports
template.ShouldExportUserPoolId("test-stack", "auth-pool");
template.ShouldExportUserPoolArn("test-stack", "auth-pool");
template.ShouldExportAppClientId("test-stack", "auth-pool", "web-app");
```

## Web Function Testing

The Web Function helpers follow the same convention as the other constructs: a fluent props builder and assertion extension methods on `Template`. Assertions match resources by **function and endpoint names**, not by logical ID, so they keep working as IDs change and can distinguish multiple endpoints on one function.

The lowest-friction setup uses an imported, externally managed artifact bucket, so a test synthesizes without AWS credentials and without an `AWS::S3::Bucket` resource.

### Props Builder

```csharp
var (_, stack) = CdkTestHelper.CreateTestStack("test-stack");

var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
    .WithFunctionName("my-web")           // default: "test-web-function"
    .WithTestArtifact(stack)              // imported bucket, no bucket resource; key defaults to "artifacts/app.zip"
    .WithIamAuthEndpoint()                // or WithApplicationManagedEndpoint()
    .WithEnvironmentVariable("NODE_ENV", "production")
    .WithEntryPoint("server.js")
    .WithTimeout(Duration.Seconds(60))
    .Build();

var web = new WebFunctionConstruct(stack, "web", props);
```

`Build()` throws an `InvalidOperationException` when code or an endpoint was never chosen: the builder never selects an authentication mode for you. Use `WithCode(WebFunctionCode.FromBucket(...))` for a real bucket and `WithEndpoint(...)` for a fully customized `WebFunctionEndpointOptions` record.

### Assertion Methods

```csharp
var template = Template.FromStack(stack);

// Function, revision and code
template.ShouldHaveWebFunction("my-web");
template.ShouldHaveRevision("my-web");                                   // runtime defaults to "nodejs24.x"
template.ShouldHaveRevisionCode("my-artifacts", "artifacts/app.zip");    // optional versionId

// Endpoints
template.ShouldHaveEndpoint("my-web", "default", WebFunctionAuthType.IamAuth);
template.ShouldHaveEndpointScaling("default", maxEnvironments: 5, rateLimit: 100); // null asserts the block is absent

// IAM and resource policy
template.ShouldHaveInvokeGrant("my-web", "default");                     // identity-policy statement
template.ShouldHaveSingleResourcePolicy("my-web");
template.ShouldAllowCloudFront("my-web", "default");
template.ShouldAllowResourcePolicyPrincipal("my-web", "default", "arn:aws:iam::222233334444:role/partner");
template.ShouldHaveNoResourcePolicy();

// CloudFront OAC and the code-bucket statement
template.ShouldHaveWebFunctionOac();                                     // signing protocol defaults to "sigv4"
template.ShouldHaveCodeBucketReadStatement("my-web");
```

The assertions require `Effect = Allow`, the expected action (`lambda:InvokeWebFunctionEndpoint`), and the exact endpoint resource, so a `Deny` statement or an unrelated action never satisfies them. `ShouldHaveInvokeGrant` also matches a minimized policy whose `Resource` is an array of endpoint ARNs.

### AutoData Support

```csharp
[Theory]
[WebFunctionConstructAutoData]
public void Should_Synthesize_Generated_Props(WebFunctionConstructProps props)
{
    var (_, stack) = CdkTestHelper.CreateTestStack();

    _ = new WebFunctionConstruct(stack, "web", props);

    Template.FromStack(stack).ShouldHaveWebFunction("test-web-function");
}
```

`[WebFunctionConstructAutoData]` supplies deterministic props whose default code artifact is an imported, externally managed bucket, so the fixture synthesizes without credentials or a bucket resource.

### Golden-Template Conventions

Representative Web Function configurations are pinned as full synthesized templates under `test/LayeredCraft.Cdk.Constructs.Tests/GoldenTemplates/` (`web-function-default.json`, `web-function-cloudfront-oac.json`, and so on), alongside the golden templates for the four existing constructs. `GoldenTemplate.AssertMatches(stack, "web-function-default.json")` compares the complete, normalized template and fails with a readable diff. Regenerate intentionally with `UPDATE_GOLDEN_TEMPLATES=1`; the harness rewrites the checked-in file and then fails once so the change is reviewed. Existing-construct goldens must show **zero diff** on any dependency or CDK bump.

### CDK/jsii Compatibility Considerations

- **Origin exceptions.** Validation that runs inside CDK's `IOrigin.Bind` path (and inside `HttpOrigin` construction) surfaces in .NET as a `System.Exception` carrying CDK's message, while factory-time checks in `WebFunctionOrigin` throw `InvalidOperationException`. Assert with the matching exception type, and prefer validating origin props at factory time.
- **Principal materialization.** Identity-policy writes and principal classification depend partly on how jsii materializes types in `Amazon.CDK.Lib`: exported classes such as `Role`, `User`, `Group`, `LazyRole` and `PrincipalWithConditions` arrive as concrete .NET types, while CDK's private classes (`ImmutableRole`, the imported role) arrive as interface proxies. The real-CDK `CdkUpgradeGuard_*` tests in `WebFunctionSecurityTests.cs` pin this behavior; they must pass before any `Amazon.CDK.Lib` upgrade is accepted.

## AutoFixture Integration

### Custom Attributes

```csharp
[Theory]
[LambdaFunctionConstructAutoData]
public void Should_Create_Lambda_With_Generated_Data(LambdaFunctionConstructProps props)
{
    // props is automatically generated with realistic test data
}

[Theory]
[LambdaFunctionConstructAutoData(includeOtelLayer: false, includePermissions: true)]
public void Should_Create_Lambda_With_Custom_Settings(LambdaFunctionConstructProps props)
{
    // props generated with specific customizations
}
```

### Available Attributes

- `LambdaFunctionConstructAutoDataAttribute`: Generates Lambda props
- `StaticSiteConstructAutoDataAttribute`: Generates static site props
- `DynamoDbTableConstructAutoDataAttribute`: Generates DynamoDB props
- `CognitoUserPoolConstructAutoDataAttribute`: Generates Cognito user pool props (pass `includeBranding: true` to include Managed Login branding)
- `WebFunctionConstructAutoDataAttribute`: Generates Web Function props (imported, externally managed artifact bucket; IAM-auth endpoint)

## Testing Patterns

### Scenario-Based Testing

```csharp
[Theory]
[InlineData(true, true)]   // OTEL explicitly enabled, permissions included
[InlineData(true, false)]  // OTEL explicitly enabled, no permissions  
[InlineData(false, true)]  // OTEL disabled (default in v2.0+), permissions included
[InlineData(false, false)] // OTEL disabled (default in v2.0+), no permissions
public void Should_Handle_Different_Configurations(bool includeOtel, bool includePermissions)
{
    var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
        .WithOtelEnabled(includeOtel);
    
    if (includePermissions)
    {
        props = props.WithApiGatewayPermission("arn:aws:execute-api:us-east-1:123456789012:abcdef123/*");
    }
    
    var builtProps = props.Build();
    
    // Test with different configurations
}
```

### Custom Stack Testing

```csharp
[Fact]
public void Should_Create_Custom_Stack()
{
    var props = new MyCustomStackProps
    {
        Environment = "test",
        ApiName = "my-api"
    };
    
    var stack = CdkTestHelper.CreateTestStackMinimal<MyCustomStack, MyCustomStackProps>("test-stack", props);
    var template = Template.FromStack(stack);
    
    // Assert custom stack resources
}
```

## Best Practices

### 1. Use Collection Attribute
Always use `[Collection("CDK Tests")]` for CDK construct tests.

### 2. Create Templates After Constructs
```csharp
// ✅ Correct
var construct = new LambdaFunctionConstruct(stack, "test", props);
var template = Template.FromStack(stack);

// ❌ Incorrect
var template = Template.FromStack(stack);
var construct = new LambdaFunctionConstruct(stack, "test", props);
```

### 3. Use Builders for Complex Props
```csharp
// ✅ Recommended
var props = CdkTestHelper.CreatePropsBuilder(assetPath)
    .WithMemorySize(512)
    .WithTimeoutInSeconds(30)
    .Build();

// ❌ Verbose
var props = new LambdaFunctionConstructProps
{
    FunctionName = "test-function",
    FunctionSuffix = "test",
    AssetPath = assetPath,
    RoleName = "test-role",
    PolicyName = "test-policy",
    MemorySize = 512,
    TimeoutInSeconds = 30
};
```

### 4. Test Both Positive and Negative Cases
```csharp
template.ShouldHaveOtelLayer();      // When enabled
template.ShouldNotHaveOtelLayer();   // When disabled
```

### 5. Use Meaningful Test Names
```csharp
[Fact]
public void Should_Enable_Tracing_When_Configured()
{
    // Clear what the test is verifying
}
```

## Common Issues

### Template Creation Timing
Create templates AFTER adding constructs to stacks, not before.

### Parallel Test Execution
CDK tests must run sequentially due to shared context. Use `[Collection("CDK Tests")]`.

### Asset Path Resolution
Use `AssetPathExtensions.GetTestLambdaZipPath()` for consistent asset paths.

### Stack Environment
Test stacks are created with us-east-1 environment by default. Override if needed for region-specific testing.

## Examples

For complete testing examples, see the test files in the repository:
- `LambdaFunctionConstructTests.cs`
- `StaticSiteConstructTests.cs`
- `DynamoDbTableConstructTests.cs`
- `CognitoUserPoolConstructTests.cs`
- `WebFunctionConstructTests.cs`, `WebFunctionSecurityTests.cs`, `WebFunctionInvokeGrantTests.cs`, `WebFunctionOriginTests.cs`
- `WebFunctionConstructTestingHelpersTests.cs`, `WebFunctionConstructGoldenTemplateTests.cs`

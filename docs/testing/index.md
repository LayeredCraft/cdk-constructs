---
layout: default
title: Testing Guide
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
    .WithSnapStart(true)
    .WithGenerateUrl(true)
    .Build();
```

### Assertion Methods

```csharp
// Basic assertions
template.ShouldHaveLambdaFunction("my-function-prod");
template.ShouldHaveMemorySize(1024);
template.ShouldHaveTimeout(30);

// Advanced features
template.ShouldHaveOtelLayer();
template.ShouldHaveSnapStart();
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

## Testing Patterns

### Scenario-Based Testing

```csharp
[Theory]
[InlineData(true, true)]   // OTEL enabled, permissions included
[InlineData(true, false)]  // OTEL enabled, no permissions
[InlineData(false, true)]  // OTEL disabled, permissions included
[InlineData(false, false)] // OTEL disabled, no permissions
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
public void Should_Enable_SnapStart_When_Configured()
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
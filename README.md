# cdk-constructs

[![Build Status](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)

A reusable library of AWS CDK constructs for .NET projects, optimized for serverless applications using Lambda, API Gateway (or Lambda URLs), DynamoDB, S3, CloudFront, and OpenTelemetry. Built for speed, observability, and cost efficiency across the LayeredCraft project ecosystem.

## Installation

Install the package via NuGet:

```bash
dotnet add package LayeredCraft.Cdk.Constructs
```

Or via Package Manager Console:

```powershell
Install-Package LayeredCraft.Cdk.Constructs
```

## Quick Start

### Basic Lambda Function

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using LayeredCraft.Cdk.Constructs.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var lambdaConstruct = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
        {
            FunctionName = "my-function",
            FunctionSuffix = "prod",
            AssetPath = "./lambda-deployment.zip",
            RoleName = "my-function-role",
            PolicyName = "my-function-policy",
            EnvironmentVariables = new Dictionary<string, string>
            {
                { "ENVIRONMENT", "production" },
                { "LOG_LEVEL", "info" }
            }
        });
    }
}
```

### Lambda with Custom IAM Permissions

```csharp
var lambdaConstruct = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-function",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-function-role",
    PolicyName = "my-function-policy",
    PolicyStatements = new[]
    {
        new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query" },
            Resources = new[] { "arn:aws:dynamodb:us-east-1:123456789012:table/MyTable" },
            Effect = Effect.ALLOW
        })
    },
    Permissions = new List<LambdaPermission>
    {
        new LambdaPermission
        {
            Principal = "apigateway.amazonaws.com",
            Action = "lambda:InvokeFunction",
            SourceArn = "arn:aws:execute-api:us-east-1:123456789012:*/*/GET/my-endpoint"
        }
    }
});
```

## Features

### 🚀 LambdaFunctionConstruct

The main construct that creates a complete Lambda function setup with:

- **Lambda Function**: Configured for custom runtimes (`PROVIDED_AL2023`) with bootstrap handler
- **IAM Role & Policy**: Automatic CloudWatch Logs permissions + custom policy statements
- **CloudWatch Logs**: Explicit log group with configurable retention (default: 2 weeks)
- **OpenTelemetry**: Optional AWS OTEL Collector layer integration
- **Versioning**: Automatic version creation with "live" alias
- **Multi-target Permissions**: Applies permissions to function, version, and alias

### 🔧 Configuration Options

| Property | Type | Description | Default |
|----------|------|-------------|---------|
| `FunctionName` | `string` | Base name for the Lambda function | Required |
| `FunctionSuffix` | `string` | Suffix appended to function name | Required |
| `AssetPath` | `string` | Path to Lambda deployment package | Required |
| `RoleName` | `string` | IAM role name | Required |
| `PolicyName` | `string` | IAM policy name | Required |
| `PolicyStatements` | `PolicyStatement[]` | Custom IAM policy statements | `[]` |
| `EnvironmentVariables` | `IDictionary<string, string>` | Environment variables | `{}` |
| `IncludeOtelLayer` | `bool` | Enable OpenTelemetry layer | `true` |
| `Permissions` | `List<LambdaPermission>` | Lambda invocation permissions | `[]` |

### 🔍 OpenTelemetry Integration

When `IncludeOtelLayer` is enabled (default), the construct automatically:
- Adds the AWS OTEL Collector layer
- Enables X-Ray tracing
- Configures the Lambda for observability

### 📋 Automatic IAM Permissions

The construct automatically grants these CloudWatch Logs permissions:
- `logs:CreateLogStream`
- `logs:CreateLogGroup`
- `logs:TagResource`
- `logs:PutLogEvents`

## Testing Support

The library includes comprehensive testing helpers to make it easy to unit test your CDK stacks that use these constructs.

### Testing Helpers Overview

- **`CdkTestHelper`**: Reduces boilerplate for creating test stacks and props (including custom stack types)
- **`LambdaFunctionConstructAssertions`**: Extension methods for asserting Lambda resources
- **`LambdaFunctionConstructPropsBuilder`**: Fluent builder for creating test props

### Quick Testing Example

```csharp
using LayeredCraft.Cdk.Constructs.Constructs;
using LayeredCraft.Cdk.Constructs.Testing;

[Fact]
public void MyStack_ShouldCreateApiFunction()
{
    // Create test infrastructure with minimal boilerplate
    var stack = CdkTestHelper.CreateTestStackMinimal();

    // Build test props using fluent builder
    var props = CdkTestHelper.CreatePropsBuilder("./my-lambda.zip")
        .WithFunctionName("my-api")
        .WithFunctionSuffix("prod")
        .WithDynamoDbAccess("users-table")
        .WithApiGatewayPermission("arn:aws:execute-api:us-east-1:123456789012:abcdef123/prod/GET/users")
        .WithEnvironmentVariable("TABLE_NAME", "users-table")
        .WithOtelEnabled(true)
        .Build();

    // Create the construct
    _ = new LambdaFunctionConstruct(stack, "MyApiLambda", props);

    // Create template AFTER adding constructs to the stack
    var template = Template.FromStack(stack);

    // Use assertion helpers for clean, readable tests
    template.ShouldHaveLambdaFunction("my-api-prod");
    template.ShouldHaveOtelLayer();
    template.ShouldHaveCloudWatchLogsPermissions("my-api-prod");
    template.ShouldHaveEnvironmentVariables(new Dictionary<string, string>
    {
        { "TABLE_NAME", "users-table" }
    });
    template.ShouldHaveLambdaPermissions(1); // 1 permission = 3 resources (function + version + alias)
    template.ShouldHaveVersionAndAlias("live");
    template.ShouldHaveLogGroup("my-api-prod", 14);
}
```

### Example: Testing Without OpenTelemetry

```csharp
[Fact]
public void MyStack_ShouldCreateLegacyFunction()
{
    var stack = CdkTestHelper.CreateTestStackMinimal();

    var props = CdkTestHelper.CreatePropsBuilder("./legacy-lambda.zip")
        .WithFunctionName("legacy-function")
        .WithOtelEnabled(false)
        .Build();

    _ = new LambdaFunctionConstruct(stack, "LegacyLambda", props);

    // Create template AFTER adding constructs to the stack
    var template = Template.FromStack(stack);

    template.ShouldHaveLambdaFunction("legacy-function-test");
    template.ShouldNotHaveOtelLayer();
    template.ShouldHaveCloudWatchLogsPermissions("legacy-function-test");
}
```

### Example: Testing Custom Stack Types

For projects with custom stack implementations (inheriting from `Stack`), you can use the generic methods to create your custom stack types directly:

```csharp
[Fact]
public void MyCustomStack_ShouldCreateInfrastructure()
{
    // Custom stack props (could be your LightsaberStackProps, InfraStackProps, etc.)
    var customProps = new MyCustomStackProps
    {
        Env = new Environment { Account = "123456789012", Region = "us-east-1" },
        Context = new MyContext { /* your custom context */ },
        Tags = new Dictionary<string, string> { { "Environment", "test" } }
    };

    // Create your custom stack type directly - no unused base stack needed!
    var customStack = CdkTestHelper.CreateTestStackMinimal<MyCustomStack>("test-stack", customProps);

    // Your custom stack is ready to use
    var template = Template.FromStack(customStack);

    // Verify your custom stack's infrastructure
    template.ShouldHaveLambdaFunction("my-function-test");
    // ... other assertions
}
```

**Benefits of Generic Stack Creation:**
- **No Workarounds**: Create custom stack types directly instead of creating unused base stacks
- **Type Safety**: Get strongly-typed stack instances with full IntelliSense support
- **Clean Tests**: Eliminate boilerplate while maintaining flexibility
- **Real-World Ready**: Works with any custom stack implementation

### Example: Testing with Custom Stack Props Types

For advanced scenarios where your custom stack expects specific props interfaces (not just `IStackProps`), you can use the dual-generic methods:

```csharp
[Fact]
public void MyAdvancedStack_ShouldWorkWithCustomProps()
{
    // Custom props interface extending IStackProps
    var customProps = new MyCustomStackProps
    {
        Env = new Environment { Account = "123456789012", Region = "us-east-1" },
        Context = new MyContext { DatabaseUrl = "test-db-url" },
        FeatureFlags = new Dictionary<string, bool> { { "EnableNewFeature", true } }
    };

    // Use dual generics for exact type matching - solves Activator.CreateInstance issues
    var stack = CdkTestHelper.CreateTestStackMinimal<MyAdvancedStack, MyCustomStackProps>(
        "advanced-test", customProps);

    // Your stack gets the exact props type it expects
    var template = Template.FromStack(stack);

    // Test with full type safety
    template.ShouldHaveLambdaFunction("advanced-function");
    stack.DatabaseUrl.Should().Be("test-db-url"); // Custom props available
    stack.IsNewFeatureEnabled.Should().Be(true);
}
```

**When to Use Dual Generics:**
- Your stack constructor expects a specific props interface (e.g., `ILightsaberStackProps`, `IInfraStackProps`)
- You need exact type matching for `Activator.CreateInstance` to work correctly
- You want full IntelliSense support for your custom props throughout the test

### Test Asset Management

Create a `TestAssets` folder in your test project and place your Lambda deployment packages there:

```
YourTestProject/
├── TestAssets/
│   ├── test-lambda.zip      # Default test asset
│   └── my-custom-lambda.zip # Custom test assets
└── YourTests.cs
```

The testing helpers automatically resolve test asset paths:

```csharp
// Uses TestAssets/test-lambda.zip by default
var props = CdkTestHelper.CreatePropsBuilder().Build();

// Or specify your own test asset
var props = CdkTestHelper.CreatePropsBuilder("./TestAssets/my-custom-lambda.zip").Build();

// Get reliable paths to test assets
var assetPath = CdkTestHelper.GetTestAssetPath("TestAssets/my-lambda.zip");
```

### Available Assertion Methods

| Method | Description |
|--------|-------------|
| `ShouldHaveLambdaFunction(functionName)` | Verify Lambda function exists |
| `ShouldHaveOtelLayer()` | Verify OpenTelemetry layer and tracing are enabled |
| `ShouldNotHaveOtelLayer()` | Verify OpenTelemetry is disabled |
| `ShouldHaveCloudWatchLogsPermissions(functionName)` | Verify log permissions |
| `ShouldHaveEnvironmentVariables(variables)` | Verify environment variables |
| `ShouldHaveLambdaPermissions(count)` | Verify Lambda invoke permissions |
| `ShouldHaveVersionAndAlias(aliasName)` | Verify versioning setup |
| `ShouldHaveLogGroup(functionName, retentionDays)` | Verify log group configuration |

### Props Builder Methods

| Method | Description |
|--------|-------------|
| `WithFunctionName(name)` | Set function name |
| `WithFunctionSuffix(suffix)` | Set function suffix |
| `WithAssetPath(path)` | Set Lambda deployment package path |
| `WithOtelEnabled(bool)` | Enable/disable OpenTelemetry |
| `WithDynamoDbAccess(tableName)` | Add DynamoDB permissions |
| `WithS3Access(bucketName)` | Add S3 permissions |
| `WithApiGatewayPermission(apiArn)` | Add API Gateway invoke permission |
| `WithAlexaPermission(skillId)` | Add Alexa Skills Kit permission |
| `WithEnvironmentVariable(key, value)` | Add environment variable |
| `WithEnvironmentVariables(variables)` | Add multiple environment variables |
| `WithCustomPolicy(policyStatement)` | Add custom IAM policy statement |
| `WithCustomPermission(permission)` | Add custom Lambda permission |

## Project Structure

```
├── src/
│   └── LayeredCraft.Cdk.Constructs/           # Main library package
│       ├── Constructs/
│       │   └── LambdaFunctionConstruct.cs      # Lambda function construct with IAM and logging
│       ├── Models/
│       │   ├── LambdaFunctionConstructProps.cs # Configuration props for Lambda construct
│       │   └── LambdaPermission.cs             # Lambda permission model
│       └── Testing/                            # Testing helpers for consumers
│           ├── CdkTestHelper.cs                # Test stack and props creation utilities
│           ├── LambdaFunctionConstructAssertions.cs # Extension methods for assertions
│           └── LambdaFunctionConstructPropsBuilder.cs # Fluent builder for test props
├── test/
│   └── LayeredCraft.Cdk.Constructs.Tests/     # Test suite
│       ├── Constructs/
│       │   └── LambdaFunctionConstructTests.cs # Unit tests for Lambda construct
│       ├── Testing/
│       │   └── TestingHelpersTests.cs          # Tests for testing helper functionality
│       ├── TestKit/                            # Test utilities and fixtures
│       │   ├── Attributes/                     # Custom AutoFixture attributes
│       │   ├── Customizations/                 # Test data customizations
│       │   └── Extensions/                     # Test extension methods
│       └── TestAssets/
│           └── test-lambda.zip                 # Test Lambda deployment package
└── LayeredCraft.Cdk.Constructs.sln            # Solution file
```

## Requirements

- **.NET 8.0 or .NET 9.0**
- **AWS CDK v2** (Amazon.CDK.Lib 2.203.1+)
- **AWS CLI** configured with appropriate permissions
- **Node.js** (for CDK deployment)

## Runtime Configuration

The Lambda functions created by this construct use:
- **Runtime**: `PROVIDED_AL2023` (Amazon Linux 2023)
- **Handler**: `bootstrap` (for custom runtimes)
- **Memory**: 1024 MB
- **Timeout**: 6 seconds
- **Log Retention**: 2 weeks

## Development

### Building the Project

```bash
# Build the solution
dotnet build

# Run tests
dotnet run --project test/LayeredCraft.Cdk.Constructs.Tests/ --framework net8.0

# Pack for local testing
dotnet pack src/LayeredCraft.Cdk.Constructs/
```

### Testing

The project uses xUnit v3 with AutoFixture for comprehensive testing:

```bash
# Run all tests
cd test/LayeredCraft.Cdk.Constructs.Tests
dotnet run --framework net8.0

# Run specific test
dotnet run --framework net8.0 -- --filter "Construct_ShouldCreateLambdaFunction"
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For issues and questions:
- 🐛 [Report bugs](https://github.com/LayeredCraft/cdk-constructs/issues)
- 💡 [Request features](https://github.com/LayeredCraft/cdk-constructs/issues)
- 📖 [Documentation](https://github.com/LayeredCraft/cdk-constructs/wiki)

---

**Built with ❤️ by the LayeredCraft team**

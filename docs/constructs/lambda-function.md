---
layout: default
title: Lambda Function Construct
---

# Lambda Function Construct

The `LambdaFunctionConstruct` provides a comprehensive, production-ready Lambda function with integrated OpenTelemetry support, IAM management, and environment configuration.

## Features

- **OpenTelemetry Integration**: Built-in AWS OpenTelemetry collector layer
- **IAM Management**: Automatic role and policy creation with CloudWatch Logs permissions
- **Environment Configuration**: Easy environment variable management
- **Function URLs**: Optional HTTP endpoint generation
- **SnapStart Support**: Improved cold start performance for Java runtimes
- **Versioning & Aliases**: Automatic version management with "live" alias
- **Lambda Permissions**: Multi-target permission management (function, version, alias)

## Basic Usage

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
        {
            FunctionName = "my-api",
            FunctionSuffix = "prod",
            AssetPath = "./lambda-deployment.zip",
            RoleName = "my-api-role",
            PolicyName = "my-api-policy"
        });
    }
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `FunctionName` | `string` | Base name of the Lambda function |
| `FunctionSuffix` | `string` | Suffix appended to function name (e.g., "prod", "dev") |
| `AssetPath` | `string` | Path to Lambda deployment package |
| `RoleName` | `string` | Name of the IAM role |
| `PolicyName` | `string` | Name of the IAM policy |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MemorySize` | `double` | `1024` | Memory allocation in MB |
| `TimeoutInSeconds` | `double` | `6` | Function timeout in seconds |
| `PolicyStatements` | `PolicyStatement[]` | `[]` | Additional IAM policy statements |
| `EnvironmentVariables` | `IDictionary<string, string>` | `{}` | Environment variables |
| `IncludeOtelLayer` | `bool` | `true` | Enable OpenTelemetry layer |
| `Permissions` | `List<LambdaPermission>` | `[]` | Lambda invocation permissions |
| `EnableSnapStart` | `bool` | `false` | Enable SnapStart for improved cold starts |
| `GenerateUrl` | `bool` | `false` | Generate Function URL for HTTP access |

## Advanced Examples

### Lambda with DynamoDB Access

```csharp
var dynamoPolicy = new PolicyStatement(new PolicyStatementProps
{
    Actions = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query"],
    Resources = ["arn:aws:dynamodb:us-east-1:123456789012:table/MyTable"],
    Effect = Effect.ALLOW
});

var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    PolicyStatements = [dynamoPolicy],
    EnvironmentVariables = new Dictionary<string, string>
    {
        { "TABLE_NAME", "MyTable" },
        { "AWS_REGION", "us-east-1" }
    }
});
```

### Lambda with Function URL

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    GenerateUrl = true, // Creates HTTP endpoint
    MemorySize = 2048,
    TimeoutInSeconds = 30
});

// Access the function URL domain
var functionUrl = lambda.LiveAliasFunctionUrlDomain;
```

### Lambda with API Gateway Permissions

```csharp
var apiPermission = new LambdaPermission
{
    Principal = "apigateway.amazonaws.com",
    Action = "lambda:InvokeFunction",
    SourceArn = "arn:aws:execute-api:us-east-1:123456789012:abcdef123/*"
};

var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    Permissions = [apiPermission]
});
```

### Lambda with SnapStart

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    EnableSnapStart = true // Improves cold start performance
});
```

## Public Properties

### LambdaFunction
Access the underlying CDK Lambda function for advanced configuration:

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", props);
var underlyingFunction = lambda.LambdaFunction;

// Add additional configuration
underlyingFunction.AddEnvironment("CUSTOM_VAR", "value");
```

### LiveAliasFunctionUrlDomain
Get the domain of the Function URL (if enabled):

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    // ... other props
    GenerateUrl = true
});

var domain = lambda.LiveAliasFunctionUrlDomain; // Returns the domain string
```

## Runtime Configuration

The Lambda functions use the following runtime configuration:

- **Runtime**: `PROVIDED_AL2023` (Amazon Linux 2023)
- **Handler**: `bootstrap` (for custom runtimes)
- **Architecture**: x86_64
- **Log Retention**: 2 weeks
- **OpenTelemetry Layer**: AWS managed layer (us-east-1 region)

## IAM Permissions

The construct automatically creates:

1. **CloudWatch Logs Permissions**: 
   - `logs:CreateLogStream`
   - `logs:CreateLogGroup`
   - `logs:TagResource`
   - `logs:PutLogEvents`

2. **Custom Policy Statements**: Any additional policies you provide

3. **Lambda Permissions**: Applied to function, version, and alias

## Versioning Strategy

- Creates a new version on every deployment
- Maintains a "live" alias pointing to the latest version
- Versions have `RemovalPolicy.RETAIN` to prevent deletion

## Testing

See the [Testing Guide](../testing/) for comprehensive testing utilities and patterns specific to the Lambda Function construct.

## Examples

For more real-world examples, see the [Examples](../examples/) section.
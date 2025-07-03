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

## Project Structure

```
├── src/
│   └── LayeredCraft.Cdk.Constructs/           # Main library package
│       ├── Constructs/
│       │   └── LambdaFunctionConstruct.cs      # Lambda function construct with IAM and logging
│       └── Models/
│           ├── LambdaFunctionConstructProps.cs # Configuration props for Lambda construct
│           └── LambdaPermission.cs             # Lambda permission model
├── test/
│   └── LayeredCraft.Cdk.Constructs.Tests/     # Test suite
│       ├── Constructs/
│       │   └── LambdaFunctionConstructTests.cs # Unit tests for Lambda construct
│       ├── TestKit/                            # Test utilities and fixtures
│       │   ├── Attributes/                     # Custom AutoFixture attributes
│       │   └── Customizations/                 # Test data customizations
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

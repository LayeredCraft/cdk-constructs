# LayeredCraft CDK Constructs

[![Build Status](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)

A comprehensive library of reusable AWS CDK constructs for .NET projects, designed for serverless applications and static websites. Built with best practices, observability, and cost efficiency in mind.

## Features

- **🚀 Lambda Functions**: Comprehensive Lambda construct with OpenTelemetry support, IAM management, and environment configuration
- **🌐 Static Sites**: Complete static website hosting with S3, CloudFront, SSL certificates, and Route53 DNS management
- **📊 DynamoDB Tables**: Full-featured DynamoDB construct with streams, TTL, and global secondary indexes
- **🧪 Testing Helpers**: Extensive testing utilities with fluent assertions and builders
- **📝 Type Safety**: Full intellisense and compile-time validation
- **⚡ Performance**: Optimized for cold starts with AWS Lambda SnapStart support

## Installation

```bash
dotnet add package LayeredCraft.Cdk.Constructs
```

## Quick Start

### Basic Lambda Function

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
            PolicyName = "my-api-policy",
            GenerateUrl = true, // Creates Function URL for HTTP access
            EnvironmentVariables = new Dictionary<string, string>
            {
                { "ENVIRONMENT", "production" },
                { "LOG_LEVEL", "info" }
            }
        });
    }
}
```

### Static Website

```csharp
var website = new StaticSiteConstruct(this, "Website", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "example.com",
    SiteSubDomain = "www",
    AssetPath = "./website-build"
});
```

### DynamoDB Table

```csharp
var table = new DynamoDbTableConstruct(this, "UserTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    GlobalSecondaryIndexes = [
        new GlobalSecondaryIndex
        {
            IndexName = "email-index",
            PartitionKey = new AttributeDefinition { AttributeName = "email", AttributeType = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        }
    ]
});
```

## Documentation

📖 **[Complete Documentation](https://layeredcraft.github.io/cdk-constructs/)**

- **[Lambda Function Construct](https://layeredcraft.github.io/cdk-constructs/constructs/lambda-function)** - Full-featured Lambda functions with OpenTelemetry, IAM, and more
- **[Static Site Construct](https://layeredcraft.github.io/cdk-constructs/constructs/static-site)** - Complete static website hosting with CloudFront and SSL
- **[DynamoDB Table Construct](https://layeredcraft.github.io/cdk-constructs/constructs/dynamodb-table)** - Production-ready DynamoDB tables with streams and indexes
- **[Testing Guide](https://layeredcraft.github.io/cdk-constructs/testing)** - Comprehensive testing utilities and patterns
- **[Examples](https://layeredcraft.github.io/cdk-constructs/examples)** - Real-world usage examples and patterns

## Requirements

- **.NET 8.0** or **.NET 9.0**
- **AWS CDK v2** (Amazon.CDK.Lib 2.203.1+)
- **AWS CLI** configured with appropriate permissions
- **Node.js** (for CDK deployment)

## Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Setup

```bash
# Clone the repository
git clone https://github.com/LayeredCraft/cdk-constructs.git
cd cdk-constructs

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet run --project test/LayeredCraft.Cdk.Constructs.Tests/ --framework net8.0
```

### Code Style

- Follow C# coding conventions
- Use meaningful names for variables and methods
- Add XML documentation for public APIs
- Include unit tests for new features
- Run tests before submitting PRs

## License

This project is licensed under the [MIT License](LICENSE).

## Support

- **Issues**: [GitHub Issues](https://github.com/LayeredCraft/cdk-constructs/issues)
- **Discussions**: [GitHub Discussions](https://github.com/LayeredCraft/cdk-constructs/discussions)
- **Documentation**: [https://layeredcraft.github.io/cdk-constructs/](https://layeredcraft.github.io/cdk-constructs/)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for details on releases and changes.

---

Built with ❤️ by the LayeredCraft team
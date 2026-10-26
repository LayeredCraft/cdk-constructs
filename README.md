# LayeredCraft CDK Constructs
<!-- ALL-CONTRIBUTORS-BADGE:START - Do not remove or modify this section -->
[![All Contributors](https://img.shields.io/badge/all_contributors-2-orange.svg?style=flat-square)](#contributors-)
<!-- ALL-CONTRIBUTORS-BADGE:END -->

[![Build Status](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)

A comprehensive library of reusable AWS CDK constructs for .NET projects, designed for serverless applications and static websites. Built with best practices, observability, and cost efficiency in mind.

## Features

- **🚀 Lambda Functions**: Comprehensive Lambda construct with configurable OpenTelemetry support, IAM management, and environment configuration
- **🕸️ Web Functions**: AWS Lambda Web Functions served from an S3 artifact, with IAM or application-managed endpoints, endpoint-scoped grants, and CloudFront OAC origin integration
- **🌐 Static Sites**: Complete static website hosting with S3, CloudFront, SSL certificates, and Route53 DNS management
- **📊 DynamoDB Tables**: Full-featured DynamoDB construct with streams, TTL, and global secondary indexes
- **🔐 Cognito User Pools**: Complete Cognito user pool with custom domains, resource servers, OAuth clients, and Managed Login branding
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
            IncludeOtelLayer = true, // Enable OpenTelemetry (disabled by default in v2.0+)
            Architecture = "arm64", // Optional: specify architecture (default: amd64)
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

### Web Function

An AWS Lambda Web Function runs an ordinary HTTP server (Next.js, ASP.NET Core, Blazor Static SSR, and so on) from a zip in a versioned S3 bucket. See the [Web Function Construct](https://layeredcraft.github.io/cdk-constructs/constructs/web-function) reference for the full configuration surface and the [Web Function Workloads](https://layeredcraft.github.io/cdk-constructs/examples/web-functions) page for per-framework recipes.

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Constructs;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // The bucket must have versioning enabled and already contain the artifact.
        var artifacts = Bucket.FromBucketName(this, "artifacts", "acme-artifacts");

        var web = new WebFunctionConstruct(this, "Web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(artifacts, "1.2.3/web.zip", new WebFunctionCodeFromBucketOptions
            {
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
            }),
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
            },
        });
    }
}
```

> **Compatibility:** Lambda Web Functions require **`Amazon.CDK.Lib` 2.274.0 or later** and a Region where the feature is available. `IamAuth` endpoints are invoked by signed (SigV4) callers; `ApplicationManaged` endpoints are publicly reachable, including behind CloudFront.

## Documentation

📖 **[Complete Documentation](https://layeredcraft.github.io/cdk-constructs/)**

- **[Lambda Function Construct](https://layeredcraft.github.io/cdk-constructs/constructs/lambda-function)** - Full-featured Lambda functions with OpenTelemetry, IAM, and more
- **[Web Function Construct](https://layeredcraft.github.io/cdk-constructs/constructs/web-function)** - Lambda Web Functions with S3 artifacts, IAM/application-managed endpoints, and CloudFront OAC
- **[Static Site Construct](https://layeredcraft.github.io/cdk-constructs/constructs/static-site)** - Complete static website hosting with CloudFront and SSL
- **[DynamoDB Table Construct](https://layeredcraft.github.io/cdk-constructs/constructs/dynamodb-table)** - Production-ready DynamoDB tables with streams and indexes
- **[Cognito User Pool Construct](https://layeredcraft.github.io/cdk-constructs/constructs/cognito-user-pool)** - Full-featured Cognito user pool with custom domains, OAuth clients, and branding
- **[Testing Guide](https://layeredcraft.github.io/cdk-constructs/testing)** - Comprehensive testing utilities and patterns
- **[Examples](https://layeredcraft.github.io/cdk-constructs/examples)** - Real-world usage examples and patterns
- **[Web Function Workloads](https://layeredcraft.github.io/cdk-constructs/examples/web-functions)** - Next.js, ASP.NET Core, and Blazor Static SSR recipes

## Requirements

- **.NET 8.0** or **.NET 9.0**
- **AWS CDK v2** (Amazon.CDK.Lib 2.274.0+)
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
## Contributors ✨

Thanks goes to these wonderful people ([emoji key](https://allcontributors.org/docs/en/emoji-key)):

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center" valign="top" width="14.28%"><a href="https://github.com/taylorbobaylor"><img src="https://avatars.githubusercontent.com/u/25238885?v=4?s=100" width="100px;" alt="Taylor Christian"/><br /><sub><b>Taylor Christian</b></sub></a><br /><a href="https://github.com/LayeredCraft/cdk-constructs/commits?author=taylorbobaylor" title="Documentation">📖</a></td>
      <td align="center" valign="top" width="14.28%"><a href="https://github.com/ncipollina"><img src="https://avatars.githubusercontent.com/u/1405469?v=4?s=100" width="100px;" alt="Nick Cipollina"/><br /><sub><b>Nick Cipollina</b></sub></a><br /><a href="https://github.com/LayeredCraft/cdk-constructs/commits?author=ncipollina" title="Code">💻</a> <a href="https://github.com/LayeredCraft/cdk-constructs/commits?author=ncipollina" title="Documentation">📖</a> <a href="#infra-ncipollina" title="Infrastructure (Hosting, Build-Tools, etc)">🚇</a></td>
    </tr>
  </tbody>
</table>

<!-- markdownlint-restore -->
<!-- prettier-ignore-end -->

<!-- ALL-CONTRIBUTORS-LIST:END -->

This project follows the [all-contributors](https://github.com/all-contributors/all-contributors) specification. Contributions of any kind welcome!

---
layout: default
title: LayeredCraft CDK Constructs
---

[![Build Status](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)

A comprehensive library of reusable AWS CDK constructs for .NET projects, designed for serverless applications and static websites. Built with best practices, observability, and cost efficiency in mind.

## Key Features

- **🚀 Lambda Functions**: Comprehensive Lambda construct with configurable OpenTelemetry support, IAM management, and environment configuration
- **🌐 Static Sites**: Complete static website hosting with S3, CloudFront, SSL certificates, and Route53 DNS management
- **📊 DynamoDB Tables**: Full-featured DynamoDB construct with streams, TTL, and global secondary indexes
- **🧪 Testing Helpers**: Extensive testing utilities with fluent assertions and builders
- **📝 Type Safety**: Full TypeScript-style intellisense and compile-time validation
- **⚡ Performance**: Optimized for cold starts with AWS Lambda SnapStart support

## Installation

```bash
dotnet add package LayeredCraft.Cdk.Constructs
```

## Quick Start

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // Create a Lambda function
        var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
        {
            FunctionName = "my-api",
            FunctionSuffix = "prod",
            AssetPath = "./lambda-deployment.zip",
            RoleName = "my-api-role",
            PolicyName = "my-api-policy",
            IncludeOtelLayer = true, // Enable OpenTelemetry (disabled by default in v2.0+)
            GenerateUrl = true // Creates a Function URL for HTTP access
        });

        // Create a static website
        var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
        {
            DomainName = "example.com",
            SiteSubDomain = "www",
            AssetPath = "./website-build"
        });
    }
}
```

## Available Constructs

### [Lambda Function Construct](constructs/lambda-function.md)

Full-featured Lambda functions with:

- Configurable OpenTelemetry integration
- IAM roles and policies
- Environment variables
- Function URLs
- SnapStart support
- Versioning and aliases

### [Static Site Construct](constructs/static-site.md)

Complete static website hosting with:

- S3 website hosting
- CloudFront CDN
- SSL certificates
- Route53 DNS
- API proxy support

### [DynamoDB Table Construct](constructs/dynamodb-table.md)

Production-ready DynamoDB tables with:

- Global secondary indexes
- DynamoDB streams
- TTL configuration
- Lambda stream integration

## Documentation

- **[Testing Guide](testing/index.md)** - Comprehensive testing utilities and patterns
- **[Examples](examples/index.md)** - Real-world usage examples and patterns

## Requirements

- **.NET 8.0** or **.NET 9.0**
- **AWS CDK v2** (Amazon.CDK.Lib 2.203.1+)
- **AWS CLI** configured with appropriate permissions
- **Node.js** (for CDK deployment)

## Contributing

See the main [README](https://github.com/LayeredCraft/cdk-constructs#contributing) for contribution guidelines.

## License

This project is licensed under the [MIT License](https://github.com/LayeredCraft/cdk-constructs/blob/main/LICENSE).

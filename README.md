# cdk-constructs

[![Build Status](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/cdk-constructs/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Cdk.Constructs.svg)](https://www.nuget.org/packages/LayeredCraft.Cdk.Constructs/)

A reusable library of AWS CDK constructs for .NET projects, optimized for serverless applications and static websites. Features comprehensive Lambda functions with OpenTelemetry support, and complete static site hosting with CloudFront CDN, SSL certificates, and DNS management. Built for speed, observability, and cost efficiency across the LayeredCraft project ecosystem.

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
using LayeredCraft.Cdk.Constructs;
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

### Lambda with SnapStart for Improved Performance

```csharp
var lambdaConstruct = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-function",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-function-role",
    PolicyName = "my-function-policy",
    EnableSnapStart = true, // Enable SnapStart for faster cold starts
    EnvironmentVariables = new Dictionary<string, string>
    {
        { "ENVIRONMENT", "production" }
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
- **SnapStart**: Optional AWS Lambda SnapStart for improved cold start performance
- **Versioning**: Automatic version creation with "live" alias
- **Multi-target Permissions**: Applies permissions to function, version, and alias

### 🌐 StaticSiteConstruct

A comprehensive construct for hosting static websites with global content delivery:

- **S3 Static Website**: Bucket configured for website hosting with proper public access
- **CloudFront Distribution**: Global CDN with custom error pages and caching optimization
- **SSL Certificate**: Automatic SSL/TLS certificate with DNS validation
- **Route53 DNS**: A records with CloudFront alias targets for the domain and alternates
- **Optional API Proxying**: Forward `/api/*` requests to a backend API domain
- **Asset Deployment**: Automatic deployment of static assets with cache invalidation

#### Quick Start - Static Site

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var staticSite = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
        {
            DomainName = "example.com",
            SiteSubDomain = "www",
            AssetPath = "./build"
        });
    }
}
```

#### Static Site with API Proxying

```csharp
var staticSite = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    SiteSubDomain = "app",
    AssetPath = "./build",
    ApiDomain = "api.example.com", // Proxy /api/* to this domain
    AlternateDomains = new[] { "example.com", "www.example.com" }
});
```

#### Static Site Configuration Options

| Property | Type | Description | Default |
|----------|------|-------------|---------|
| `DomainName` | `string` | Root domain name (e.g., "example.com") | Required |
| `SiteSubDomain` | `string` | Subdomain for the site (e.g., "www", "app") | Required |
| `AssetPath` | `string` | Path to static assets directory | Required |
| `ApiDomain` | `string?` | API domain for /api/* proxying | `null` |
| `AlternateDomains` | `string[]` | Additional domains to serve content | `[]` |

### 🗃️ DynamoDbTableConstruct

A comprehensive construct for creating DynamoDB tables with advanced configuration:

- **Flexible Key Schema**: Support for partition keys, sort keys, and composite keys
- **Global Secondary Indexes**: Easy GSI configuration with automatic outputs
- **DynamoDB Streams**: Real-time data processing with Lambda integration
- **Time-to-Live (TTL)**: Automatic data expiration for cost optimization
- **CloudFormation Outputs**: Automatic export of table ARN, name, and stream ARN
- **Stream Lambda Integration**: Built-in method for attaching Lambda functions to streams
- **Billing Flexibility**: Support for both PAY_PER_REQUEST and PROVISIONED billing modes

#### Quick Start - DynamoDB Table

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
        {
            TableName = "users",
            PartitionKey = new AttributeDefinition { Name = "userId", Type = AttributeType.STRING },
            SortKey = new AttributeDefinition { Name = "itemId", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }
}
```

#### DynamoDB Table with Global Secondary Index

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new AttributeDefinition { Name = "userId", Type = AttributeType.STRING },
    SortKey = new AttributeDefinition { Name = "itemId", Type = AttributeType.STRING },
    BillingMode = BillingMode.PAY_PER_REQUEST,
    RemovalPolicy = RemovalPolicy.DESTROY,
    GlobalSecondaryIndexes = new[]
    {
        new GlobalSecondaryIndexProps
        {
            IndexName = "GSI1",
            PartitionKey = new AttributeDefinition { Name = "itemType", Type = AttributeType.STRING },
            SortKey = new AttributeDefinition { Name = "createdAt", Type = AttributeType.STRING }
        }
    }
});
```

#### DynamoDB Table with Streams and Lambda Integration

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new AttributeDefinition { Name = "aggregateId", Type = AttributeType.STRING },
    SortKey = new AttributeDefinition { Name = "eventId", Type = AttributeType.STRING },
    BillingMode = BillingMode.PAY_PER_REQUEST,
    RemovalPolicy = RemovalPolicy.DESTROY,
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    TimeToLiveAttribute = "expiresAt"
});

// Attach a Lambda function to process stream events
var streamProcessor = new LambdaFunctionConstruct(this, "StreamProcessor", new LambdaFunctionConstructProps
{
    FunctionName = "stream-processor",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "stream-processor-role",
    PolicyName = "stream-processor-policy"
});

table.AttachStreamLambda(streamProcessor.Function);
```

#### DynamoDB Configuration Options

| Property | Type | Description | Default |
|----------|------|-------------|---------|
| `TableName` | `string` | Name of the DynamoDB table | Required |
| `PartitionKey` | `IAttribute?` | Partition key attribute definition | `null` |
| `SortKey` | `IAttribute?` | Sort key attribute definition | `null` |
| `RemovalPolicy` | `RemovalPolicy` | Stack removal policy | Required |
| `BillingMode` | `BillingMode` | Billing mode (PAY_PER_REQUEST or PROVISIONED) | Required |
| `GlobalSecondaryIndexes` | `GlobalSecondaryIndexProps[]` | Array of GSI configurations | `[]` |
| `Stream` | `StreamViewType?` | Stream view type if streams enabled | `null` |
| `TimeToLiveAttribute` | `string?` | TTL attribute name | `null` |

### 🔧 Lambda Configuration Options

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
| `EnableSnapStart` | `bool` | Enable AWS Lambda SnapStart | `false` |
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
- **`StaticSiteConstructAssertions`**: Extension methods for asserting static site resources
- **`StaticSiteConstructPropsBuilder`**: Fluent builder for creating static site test props

### Quick Testing Example

```csharp
using LayeredCraft.Cdk.Constructs;
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

### Example: Testing StaticSiteConstruct

```csharp
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Testing;

[Fact]
public void MyStack_ShouldCreateStaticSiteWithApiProxy()
{
    // Create test infrastructure
    var stack = CdkTestHelper.CreateTestStackMinimal();

    // Build test props using fluent builder
    var props = CdkTestHelper.CreateStaticSitePropsBuilder()
        .WithDomainName("myapp.com")
        .WithSiteSubDomain("app")
        .WithApiDomain("api.myapp.com")
        .WithAlternateDomains("myapp.com", "www.myapp.com")
        .Build();

    // Create the construct
    _ = new StaticSiteConstruct(stack, "MyAppSite", props);

    // Create template AFTER adding constructs to the stack
    var template = Template.FromStack(stack);

    // Use assertion helpers for clean, readable tests
    template.ShouldHaveCompleteStaticSite("app.myapp.com", "app.myapp.com", 
        hasApiProxy: true, domainCount: 3);
    template.ShouldHaveApiProxyBehavior("api.myapp.com");
    template.ShouldHaveRoute53Records("app.myapp.com");
    template.ShouldHaveRoute53Records("myapp.com");
    template.ShouldHaveRoute53Records("www.myapp.com");
}
```

### Example: Testing Static Site Scenarios

```csharp
[Fact]
public void MyStack_ShouldCreateBlogSite()
{
    var stack = CdkTestHelper.CreateTestStackMinimal();

    var props = CdkTestHelper.CreateStaticSitePropsBuilder()
        .ForBlog("myblog.com")
        .Build();

    _ = new StaticSiteConstruct(stack, "BlogSite", props);

    var template = Template.FromStack(stack);

    template.ShouldHaveCompleteStaticSite("www.myblog.com", "www.myblog.com", 
        hasApiProxy: false, domainCount: 2);
    template.ShouldHaveRoute53Records("www.myblog.com");
    template.ShouldHaveRoute53Records("myblog.com");
}
```

### Example: Testing DynamoDbTableConstruct

```csharp
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Testing;

[Fact]
public void MyStack_ShouldCreateDynamoDbTable()
{
    // Create test infrastructure
    var stack = CdkTestHelper.CreateTestStackMinimal();

    // Build test props using fluent builder
    var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
        .WithTableName("users")
        .WithPartitionKey("userId")
        .WithSortKey("itemId")
        .WithGlobalSecondaryIndex("GSI1", "itemType", AttributeType.STRING, "createdAt")
        .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
        .WithTimeToLiveAttribute("expiresAt")
        .Build();

    // Create the construct
    _ = new DynamoDbTableConstruct(stack, "UsersTable", props);

    // Create template AFTER adding constructs to the stack
    var template = Template.FromStack(stack);

    // Use assertion helpers for clean, readable tests
    template.ShouldHaveDynamoTable("users");
    template.ShouldHavePartitionKey("userId", AttributeType.STRING);
    template.ShouldHaveSortKey("itemId", AttributeType.STRING);
    template.ShouldHaveGlobalSecondaryIndex("GSI1");
    template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
    template.ShouldHaveTimeToLiveAttribute("expiresAt");
    template.ShouldHaveTableOutputs("test-stack", "UsersTable");
}
```

### Example: Testing DynamoDB Table Scenarios

```csharp
[Fact]
public void MyStack_ShouldCreateSessionTable()
{
    var stack = CdkTestHelper.CreateTestStackMinimal();

    var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
        .ForSessionTable("user-sessions")
        .Build();

    _ = new DynamoDbTableConstruct(stack, "SessionsTable", props);

    var template = Template.FromStack(stack);

    template.ShouldHaveDynamoTable("user-sessions");
    template.ShouldHavePartitionKey("sessionId", AttributeType.STRING);
    template.ShouldHaveTimeToLiveAttribute("expiresAt");
    template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
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
- **Constructor Support**: Supports both public and internal constructors (CDK default)

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

### Supported Constructor Patterns

The generic methods work with both public and internal constructors using reflection with `BindingFlags`:

```csharp
// ✅ Works - Public constructor
public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props) : base(scope, id, props) { }
}

// ✅ Works - Internal constructor (CDK default pattern)
public class MyStack : Stack
{
    internal MyStack(Construct scope, string id, IStackProps props) : base(scope, id, props) { }
}

// ✅ Works - Custom props interface with internal constructor
public class MyStack : Stack
{
    internal MyStack(Construct scope, string id, IMyCustomProps props) : base(scope, id, props) { }
}
```

**Important Notes:**
- The helpers use `BindingFlags.NonPublic` to access internal constructors
- This follows common testing library patterns for accessing non-public members
- Works seamlessly with CDK's default internal constructor pattern

### Test Asset Management

Create a `TestAssets` folder in your test project and place your deployment packages there:

```
YourTestProject/
├── TestAssets/
│   ├── test-lambda.zip      # Default Lambda test asset
│   ├── my-custom-lambda.zip # Custom Lambda test assets
│   └── static-site/         # Static site test assets
│       ├── index.html
│       ├── styles.css
│       ├── about.html
│       └── app.js
└── YourTests.cs
```

The testing helpers automatically resolve test asset paths:

```csharp
// Lambda testing - uses TestAssets/test-lambda.zip by default
var lambdaProps = CdkTestHelper.CreatePropsBuilder().Build();

// Static site testing - uses TestAssets/static-site by default
var staticProps = CdkTestHelper.CreateStaticSitePropsBuilder().Build();

// Or specify your own test assets
var customLambdaProps = CdkTestHelper.CreatePropsBuilder("./TestAssets/my-custom-lambda.zip").Build();
var customStaticProps = CdkTestHelper.CreateStaticSitePropsBuilder("./TestAssets/my-site").Build();

// Get reliable paths to test assets
var lambdaAssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/my-lambda.zip");
var staticAssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/my-site");
```

### Available Assertion Methods

#### Lambda Function Assertions

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
| `ShouldHaveSnapStart()` | Verify SnapStart is enabled |
| `ShouldNotHaveSnapStart()` | Verify SnapStart is disabled |

#### Static Site Assertions

| Method | Description |
|--------|-------------|
| `ShouldHaveCompleteStaticSite(bucketName, domainName, hasApiProxy, domainCount)` | Verify complete static site setup |
| `ShouldHaveStaticWebsiteBucket(bucketName)` | Verify S3 bucket with website configuration |
| `ShouldHaveCloudFrontDistribution(domainName)` | Verify CloudFront distribution |
| `ShouldHaveSSLCertificate(domainName)` | Verify SSL certificate |
| `ShouldHaveRoute53Records(domainName)` | Verify Route53 A records |
| `ShouldHaveMultipleRoute53Records(count)` | Verify multiple domain records |
| `ShouldHaveApiProxyBehavior(apiDomain)` | Verify API proxy behavior |
| `ShouldNotHaveApiProxyBehavior()` | Verify no API proxy behavior |
| `ShouldHaveBucketDeployment()` | Verify asset deployment |

#### DynamoDB Table Assertions

| Method | Description |
|--------|-------------|
| `ShouldHaveDynamoTable(tableName)` | Verify DynamoDB table exists |
| `ShouldHavePartitionKey(keyName, keyType)` | Verify partition key configuration |
| `ShouldHaveSortKey(keyName, keyType)` | Verify sort key configuration |
| `ShouldHaveGlobalSecondaryIndex(indexName)` | Verify GSI exists |
| `ShouldHaveTableStream(streamType)` | Verify stream configuration |
| `ShouldHaveTimeToLiveAttribute(attributeName)` | Verify TTL attribute |
| `ShouldHaveTableOutputs(stackName, constructId)` | Verify CloudFormation outputs |
| `ShouldHaveGlobalSecondaryIndexCount(count)` | Verify number of GSIs |

### Props Builder Methods

#### Lambda Function Props Builder

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
| `WithSnapStart(bool)` | Enable/disable Lambda SnapStart |

#### Static Site Props Builder

| Method | Description |
|--------|-------------|
| `WithDomainName(domain)` | Set root domain name |
| `WithSiteSubDomain(subdomain)` | Set site subdomain |
| `WithAssetPath(path)` | Set static assets directory path |
| `WithApiDomain(apiDomain)` | Set API domain for proxying |
| `WithoutApiDomain()` | Remove API domain |
| `WithAlternateDomain(domain)` | Add single alternate domain |
| `WithAlternateDomains(domains...)` | Add multiple alternate domains |
| `WithoutAlternateDomains()` | Clear all alternate domains |
| `ForBlog(domain)` | Configure for blog scenario (www + naked domain) |
| `ForDocumentation(domain, apiDomain)` | Configure for docs scenario with API |
| `ForSinglePageApp(domain, apiDomain)` | Configure for SPA scenario with API |

#### DynamoDB Table Props Builder

| Method | Description |
|--------|-------------|
| `WithTableName(name)` | Set table name |
| `WithPartitionKey(keyName, keyType)` | Set partition key |
| `WithSortKey(keyName, keyType)` | Set sort key |
| `WithRemovalPolicy(policy)` | Set removal policy |
| `WithBillingMode(mode)` | Set billing mode |
| `WithGlobalSecondaryIndex(indexName, partitionKey, partitionKeyType, sortKey, sortKeyType)` | Add GSI |
| `WithStream(streamViewType)` | Enable streams |
| `WithoutStream()` | Disable streams |
| `WithTimeToLiveAttribute(attributeName)` | Set TTL attribute |
| `WithoutTimeToLiveAttribute()` | Remove TTL attribute |
| `ForUserTable(tableName)` | Configure for user data scenario |
| `ForSessionTable(tableName)` | Configure for session management scenario |
| `ForEventSourcing(tableName)` | Configure for event sourcing scenario |

## Project Structure

```
├── src/
│   └── LayeredCraft.Cdk.Constructs/           # Main library package
│       ├── LambdaFunctionConstruct.cs          # Lambda function construct with IAM and logging
│       ├── StaticSiteConstruct.cs              # Static site construct with CDN and DNS
│       ├── DynamoDbTableConstruct.cs           # DynamoDB table construct with GSI and streams
│       ├── Extensions/
│       │   └── StackExtensions.cs              # CDK Stack extension methods
│       ├── Models/
│       │   ├── LambdaFunctionConstructProps.cs # Configuration props for Lambda construct
│       │   ├── LambdaPermission.cs             # Lambda permission model
│       │   ├── StaticSiteConstructProps.cs     # Configuration props for static site construct
│       │   └── DynamoDbTableConstructProps.cs  # Configuration props for DynamoDB table construct
│       └── Testing/                            # Testing helpers for consumers
│           ├── CdkTestHelper.cs                # Test stack and props creation utilities
│           ├── LambdaFunctionConstructAssertions.cs # Extension methods for Lambda assertions
│           ├── LambdaFunctionConstructPropsBuilder.cs # Fluent builder for Lambda test props
│           ├── StaticSiteConstructAssertions.cs # Extension methods for static site assertions
│           ├── StaticSiteConstructPropsBuilder.cs # Fluent builder for static site test props
│           ├── DynamoDbTableConstructAssertions.cs # Extension methods for DynamoDB table assertions
│           └── DynamoDbTableConstructPropsBuilder.cs # Fluent builder for DynamoDB table test props
├── test/
│   └── LayeredCraft.Cdk.Constructs.Tests/     # Test suite
│       ├── LambdaFunctionConstructTests.cs     # Unit tests for Lambda construct
│       ├── StaticSiteConstructTests.cs         # Unit tests for static site construct
│       ├── DynamoDbTableConstructTests.cs      # Unit tests for DynamoDB table construct
│       ├── StackExtensionsTests.cs             # Unit tests for Stack extension methods
│       ├── Testing/
│       │   └── TestingHelpersTests.cs          # Tests for testing helper functionality
│       ├── TestKit/                            # Test utilities and fixtures
│       │   ├── Attributes/                     # Custom AutoFixture attributes
│       │   ├── Customizations/                 # Test data customizations
│       │   └── Extensions/                     # Test extension methods
│       └── TestAssets/
│           ├── test-lambda.zip                 # Test Lambda deployment package
│           └── static-site/                    # Test static site assets
│               ├── index.html
│               ├── styles.css
│               ├── about.html
│               └── app.js
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

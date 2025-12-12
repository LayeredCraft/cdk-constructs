---
layout: default
title: Examples
permalink: /examples/
---

# Examples

Real-world usage examples and patterns for LayeredCraft CDK Constructs.

## Complete Application Examples

### Serverless API with Database

A complete serverless API with Lambda, DynamoDB, and API Gateway.

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class ServerlessApiStack : Stack
{
    public ServerlessApiStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // Create DynamoDB table for user data
        var userTable = new DynamoDbTableConstruct(this, "UserTable", new DynamoDbTableConstructProps
        {
            TableName = "users",
            PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
            GlobalSecondaryIndexes = [
                new GlobalSecondaryIndexProps
                {
                    IndexName = "email-index",
                    PartitionKey = new Attribute { Name = "email", Type = AttributeType.STRING },
                    ProjectionType = ProjectionType.ALL
                }
            ],
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        // Create Lambda function for API
        var apiLambda = new LambdaFunctionConstruct(this, "ApiLambda", new LambdaFunctionConstructProps
        {
            FunctionName = "user-api",
            FunctionSuffix = "prod",
            AssetPath = "./api-lambda.zip",
            RoleName = "user-api-role",
            PolicyName = "user-api-policy",
            MemorySize = 1024,
            TimeoutInSeconds = 30,
            IncludeOtelLayer = true, // Enable OpenTelemetry for observability
            PolicyStatements = [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query", "dynamodb:UpdateItem"],
                    Resources = [userTable.TableArn, $"{userTable.TableArn}/index/*"],
                    Effect = Effect.ALLOW
                })
            ],
            EnvironmentVariables = new Dictionary<string, string>
            {
                { "TABLE_NAME", userTable.TableName },
                { "EMAIL_INDEX", "email-index" }
            },
            GenerateUrl = true // Enable Function URL for direct HTTP access
        });
    }
}
```

### Static Website with API Backend

A static website with API backend proxying.

```csharp
public class WebsiteStack : Stack
{
    public WebsiteStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // Create API Lambda
        var apiLambda = new LambdaFunctionConstruct(this, "ApiLambda", new LambdaFunctionConstructProps
        {
            FunctionName = "website-api",
            FunctionSuffix = "prod",
            AssetPath = "./api-lambda.zip",
            RoleName = "website-api-role",
            PolicyName = "website-api-policy",
            GenerateUrl = true
        });

        // Create static website with API proxy
        var website = new StaticSiteConstruct(this, "Website", new StaticSiteConstructProps
        {
            DomainName = "mywebsite.com",
            SiteSubDomain = "www",
            ApiDomain = apiLambda.LiveAliasFunctionUrlDomain!, // Proxy /api/* to Lambda
            AssetPath = "./website-build"
        });
    }
}
```

### Event-Driven Architecture

Event sourcing with DynamoDB streams and Lambda processors.

```csharp
public class EventDrivenStack : Stack
{
    public EventDrivenStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // Event store table
        var eventTable = new DynamoDbTableConstruct(this, "EventTable", new DynamoDbTableConstructProps
        {
            TableName = "events",
            PartitionKey = new Attribute { Name = "aggregateId", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "timestamp", Type = AttributeType.NUMBER },
            Stream = StreamViewType.NEW_AND_OLD_IMAGES,
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        // Read model table
        var readModelTable = new DynamoDbTableConstruct(this, "ReadModelTable", new DynamoDbTableConstructProps
        {
            TableName = "user-projections",
            PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        // Event processor Lambda
        var eventProcessor = new LambdaFunctionConstruct(this, "EventProcessor", new LambdaFunctionConstructProps
        {
            FunctionName = "event-processor",
            FunctionSuffix = "prod",
            AssetPath = "./event-processor.zip",
            RoleName = "event-processor-role",
            PolicyName = "event-processor-policy",
            PolicyStatements = [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = ["dynamodb:PutItem", "dynamodb:UpdateItem"],
                    Resources = [readModelTable.TableArn],
                    Effect = Effect.ALLOW
                })
            ],
            EnvironmentVariables = new Dictionary<string, string>
            {
                { "READ_MODEL_TABLE", readModelTable.TableName }
            }
        });

        // Connect stream to processor
        eventTable.AttachStreamLambda(eventProcessor.LambdaFunction);
    }
}
```

## Common Patterns

### Lambda with Multiple AWS Services

```csharp
var lambda = new LambdaFunctionConstruct(this, "MultiServiceLambda", new LambdaFunctionConstructProps
{
    FunctionName = "multi-service",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "multi-service-role",
    PolicyName = "multi-service-policy",
    PolicyStatements = [
        // DynamoDB access
        new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query"],
            Resources = ["arn:aws:dynamodb:us-east-1:123456789012:table/MyTable"],
            Effect = Effect.ALLOW
        }),
        // S3 access
        new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["s3:GetObject", "s3:PutObject"],
            Resources = ["arn:aws:s3:::my-bucket/*"],
            Effect = Effect.ALLOW
        }),
        // SES access
        new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["ses:SendEmail"],
            Resources = ["*"],
            Effect = Effect.ALLOW
        })
    ],
    EnvironmentVariables = new Dictionary<string, string>
    {
        { "TABLE_NAME", "MyTable" },
        { "BUCKET_NAME", "my-bucket" },
        { "FROM_EMAIL", "noreply@mycompany.com" }
    }
});
```

### Lambda with API Gateway Integration

```csharp
// Lambda function
var apiLambda = new LambdaFunctionConstruct(this, "ApiLambda", new LambdaFunctionConstructProps
{
    FunctionName = "api-handler",
    FunctionSuffix = "prod",
    AssetPath = "./api-lambda.zip",
    RoleName = "api-handler-role",
    PolicyName = "api-handler-policy",
    Permissions = [
        new LambdaPermission
        {
            Principal = "apigateway.amazonaws.com",
            Action = "lambda:InvokeFunction",
            SourceArn = "arn:aws:execute-api:us-east-1:123456789012:*/*/GET/users"
        },
        new LambdaPermission
        {
            Principal = "apigateway.amazonaws.com",
            Action = "lambda:InvokeFunction",
            SourceArn = "arn:aws:execute-api:us-east-1:123456789012:*/*/POST/users"
        }
    ]
});

// API Gateway would be created separately using standard CDK constructs
```

### Static Site with Multiple Domains

```csharp
var website = new StaticSiteConstruct(this, "Website", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "mycompany.com",
    SiteSubDomain = "www",
    AlternateDomains = [
        "mycompany.org",
        "www.mycompany.org",
        "mycompany.net",
        "www.mycompany.net"
    ],
    AssetPath = "./website-build"
});
```

### DynamoDB with Complex Indexing

```csharp
var table = new DynamoDbTableConstruct(this, "ComplexTable", new DynamoDbTableConstructProps
{
    TableName = "user-activities",
    PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "timestamp", Type = AttributeType.NUMBER },
    GlobalSecondaryIndexes = [
        // Query by activity type
        new GlobalSecondaryIndexProps
        {
            IndexName = "activity-type-index",
            PartitionKey = new Attribute { Name = "activityType", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "timestamp", Type = AttributeType.NUMBER },
            ProjectionType = ProjectionType.ALL
        },
        // Query by status
        new GlobalSecondaryIndexProps
        {
            IndexName = "status-index",
            PartitionKey = new Attribute { Name = "status", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "timestamp", Type = AttributeType.NUMBER },
            ProjectionType = ProjectionType.KEYS_ONLY
        }
    ],
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    TimeToLiveAttribute = "expiresAt",
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

## OpenTelemetry Configuration Examples

### Basic OTEL Enablement

```csharp
var lambda = new LambdaFunctionConstruct(this, "BasicOtelLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    IncludeOtelLayer = true // Enable OpenTelemetry (disabled by default in v2.0+)
});
```

### ARM64 Architecture with OTEL

```csharp
var lambda = new LambdaFunctionConstruct(this, "Arm64OtelLambda", new LambdaFunctionConstructProps
{
    FunctionName = "arm64-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "arm64-api-role",
    PolicyName = "arm64-api-policy",
    IncludeOtelLayer = true,
    Architecture = "arm64",           // Use ARM64 for better cost/performance
    OtelLayerVersion = "0-117-0"     // Specify exact OTEL version
});
```

### Different OTEL Layer Versions

```csharp
// Production with latest stable OTEL
var prodLambda = new LambdaFunctionConstruct(this, "ProdLambda", new LambdaFunctionConstructProps
{
    FunctionName = "prod-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "prod-api-role",
    PolicyName = "prod-api-policy",
    IncludeOtelLayer = true,
    OtelLayerVersion = "0-117-0"     // Latest stable version
});

// Development with specific OTEL version for consistency
var devLambda = new LambdaFunctionConstruct(this, "DevLambda", new LambdaFunctionConstructProps
{
    FunctionName = "dev-api",
    FunctionSuffix = "dev",
    AssetPath = "./lambda.zip",
    RoleName = "dev-api-role",
    PolicyName = "dev-api-policy",
    IncludeOtelLayer = true,
    OtelLayerVersion = "0-115-0"     // Previous version for testing
});
```

### Migration from v1.x Example

```csharp
// v1.x approach (OTEL enabled by default)
// var lambda = new LambdaFunctionConstruct(this, "Lambda", props);

// v2.0+ approach (OTEL must be explicitly enabled)
var lambda = new LambdaFunctionConstruct(this, "Lambda", new LambdaFunctionConstructProps
{
    FunctionName = "migrated-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "migrated-api-role",
    PolicyName = "migrated-api-policy",
    IncludeOtelLayer = true,         // Must be explicit in v2.0+
    Architecture = "amd64",          // Default, but now configurable
    OtelLayerVersion = "0-117-0"     // Latest version
});
```

### Environment-Specific OTEL Configuration

```csharp
public class OtelConfig
{
    public bool EnableOtel { get; set; }
    public string Architecture { get; set; } = "amd64";
    public string OtelVersion { get; set; } = "0-117-0";
}

public void CreateLambda(string environment, OtelConfig otelConfig)
{
    var lambda = new LambdaFunctionConstruct(this, $"{environment}Lambda", new LambdaFunctionConstructProps
    {
        FunctionName = "my-api",
        FunctionSuffix = environment,
        AssetPath = "./lambda.zip",
        RoleName = $"my-api-{environment}-role",
        PolicyName = $"my-api-{environment}-policy",
        IncludeOtelLayer = otelConfig.EnableOtel,
        Architecture = otelConfig.Architecture,
        OtelLayerVersion = otelConfig.OtelVersion,
        EnvironmentVariables = new Dictionary<string, string>
        {
            { "ENVIRONMENT", environment },
            { "OTEL_ENABLED", otelConfig.EnableOtel.ToString() }
        }
    });
}

// Usage:
// CreateLambda("prod", new OtelConfig { EnableOtel = true, Architecture = "arm64" });
// CreateLambda("dev", new OtelConfig { EnableOtel = false }); // No observability costs in dev
```

## Testing Examples

### Complete Test Suite

```csharp
[Collection("CDK Tests")]
public class ServerlessApiStackTests
{
    [Fact]
    public void Should_Create_Complete_Api_Stack()
    {
        // Arrange
        var app = new App();
        var stack = new ServerlessApiStack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });

        // Act
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveDynamoTable("users");
        template.ShouldHaveGlobalSecondaryIndex("email-index", "email", AttributeType.STRING);
        template.ShouldHaveLambdaFunction("user-api-prod");
        template.ShouldHaveFunctionUrl();
        template.ShouldHaveEnvironmentVariables(new Dictionary<string, string>
        {
            { "TABLE_NAME", "users" },
            { "EMAIL_INDEX", "email-index" }
        });
    }
}
```

### Parameterized Testing

```csharp
[Collection("CDK Tests")]
public class EnvironmentSpecificTests
{
    [Theory]
    [InlineData("dev", 512, 10)]
    [InlineData("staging", 1024, 15)]
    [InlineData("prod", 2048, 30)]
    public void Should_Configure_Lambda_For_Environment(string env, int memory, int timeout)
    {
        // Arrange
        var props = CdkTestHelper.CreatePropsBuilder(AssetPathExtensions.GetTestLambdaZipPath())
            .WithFunctionSuffix(env)
            .WithMemorySize(memory)
            .WithTimeoutInSeconds(timeout)
            .Build();

        var (app, stack) = CdkTestHelper.CreateTestStack("test-stack");

        // Act
        var construct = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveLambdaFunction($"test-function-{env}");
        template.ShouldHaveMemorySize(memory);
        template.ShouldHaveTimeout(timeout);
    }
}
```

## Best Practices Examples

### Environment-Specific Configuration

```csharp
public class EnvironmentConfig
{
    public string Environment { get; set; }
    public int LambdaMemory { get; set; }
    public int LambdaTimeout { get; set; }
    public bool EnableTracing { get; set; }
    public string DomainName { get; set; }
}

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, EnvironmentConfig config, IStackProps props = null) 
        : base(scope, id, props)
    {
        var lambda = new LambdaFunctionConstruct(this, "Lambda", new LambdaFunctionConstructProps
        {
            FunctionName = "my-function",
            FunctionSuffix = config.Environment,
            AssetPath = "./lambda.zip",
            RoleName = $"my-function-{config.Environment}-role",
            PolicyName = $"my-function-{config.Environment}-policy",
            MemorySize = config.LambdaMemory,
            TimeoutInSeconds = config.LambdaTimeout,
            IncludeOtelLayer = config.EnableTracing,
            Architecture = "amd64" // Configurable architecture
        });

        if (!string.IsNullOrEmpty(config.DomainName))
        {
            var site = new StaticSiteConstruct(this, "Site", new StaticSiteConstructProps
            {
                SiteBucketName = $"my-site-{config.Environment}",
                DomainName = config.DomainName,
                AssetPath = "./site-build"
            });
        }
    }
}
```

### Resource Naming Conventions

```csharp
public class ResourceNaming
{
    private readonly string _environment;
    private readonly string _application;

    public ResourceNaming(string application, string environment)
    {
        _application = application;
        _environment = environment;
    }

    public string Lambda(string component) => $"{_application}-{component}";
    public string LambdaRole(string component) => $"{_application}-{component}-{_environment}-role";
    public string Table(string entity) => $"{_application}-{entity}-{_environment}";
    public string Bucket(string purpose) => $"{_application}-{purpose}-{_environment}";
}

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var naming = new ResourceNaming("myapp", "prod");

        var lambda = new LambdaFunctionConstruct(this, "Lambda", new LambdaFunctionConstructProps
        {
            FunctionName = naming.Lambda("api"),
            FunctionSuffix = "prod",
            AssetPath = "./lambda.zip",
            RoleName = naming.LambdaRole("api"),
            PolicyName = naming.LambdaRole("api").Replace("-role", "-policy")
        });
    }
}
```

## Deployment Examples

### Multi-Stack Application

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        var app = new App();
        var env = new Amazon.CDK.Environment
        {
            Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
            Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
        };

        // Infrastructure stack
        var infrastructure = new InfrastructureStack(app, "MyApp-Infrastructure", new StackProps { Env = env });

        // API stack (depends on infrastructure)
        var api = new ApiStack(app, "MyApp-Api", new ApiStackProps 
        { 
            Env = env,
            UserTable = infrastructure.UserTable
        });

        // Website stack (depends on API)
        var website = new WebsiteStack(app, "MyApp-Website", new WebsiteStackProps 
        { 
            Env = env,
            ApiUrl = api.ApiUrl
        });

        app.Synth();
    }
}
```

For more examples, see the test files in the [repository](https://github.com/LayeredCraft/cdk-constructs/tree/main/test) which demonstrate comprehensive usage patterns.

---
layout: default
title: Examples
---

# Examples

Real-world usage examples and patterns for LayeredCraft CDK Constructs.

## Complete Application Examples

### Serverless API with Database

A complete serverless API with Lambda, DynamoDB, and API Gateway.

```csharp
using Amazon.CDK;
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
            SiteBucketName = "my-website-bucket",
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
            PartitionKey = new AttributeDefinition { AttributeName = "aggregateId", AttributeType = AttributeType.STRING },
            SortKey = new AttributeDefinition { AttributeName = "timestamp", AttributeType = AttributeType.NUMBER },
            StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES
        });

        // Read model table
        var readModelTable = new DynamoDbTableConstruct(this, "ReadModelTable", new DynamoDbTableConstructProps
        {
            TableName = "user-projections",
            PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING }
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
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "timestamp", AttributeType = AttributeType.NUMBER },
    GlobalSecondaryIndexes = [
        // Query by activity type
        new GlobalSecondaryIndex
        {
            IndexName = "activity-type-index",
            PartitionKey = new AttributeDefinition { AttributeName = "activityType", AttributeType = AttributeType.STRING },
            SortKey = new AttributeDefinition { AttributeName = "timestamp", AttributeType = AttributeType.NUMBER },
            ProjectionType = ProjectionType.ALL
        },
        // Query by status
        new GlobalSecondaryIndex
        {
            IndexName = "status-index",
            PartitionKey = new AttributeDefinition { AttributeName = "status", AttributeType = AttributeType.STRING },
            SortKey = new AttributeDefinition { AttributeName = "timestamp", AttributeType = AttributeType.NUMBER },
            ProjectionType = ProjectionType.KEYS_ONLY
        }
    ],
    StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES,
    TimeToLiveAttribute = "expiresAt"
});
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
            IncludeOtelLayer = config.EnableTracing
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
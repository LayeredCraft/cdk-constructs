# Lambda Function Construct

The `LambdaFunctionConstruct` provides a comprehensive, production-ready Lambda function with configurable OpenTelemetry support, IAM management, and environment configuration.

## :rocket: Features

- **:chart_with_upwards_trend: OpenTelemetry Integration**: Configurable AWS OpenTelemetry collector layer with version and architecture support
- **:shield: IAM Management**: Automatic role and policy creation with a CloudWatch Logs policy scoped to the construct's own log group
- **:gear: Environment Configuration**: Easy environment variable management
- **:link: Function URLs**: Optional HTTP endpoint generation
- **:rocket: SnapStart Support**: Improved cold start performance for Java runtimes
- **:label: Versioning & Aliases**: Automatic version management with a configurable alias name (default `"live"`)
- **:key: Lambda Permissions**: Permission targets per permission (function, version, alias) with an alias-only default
- **:package: Artifact Sources**: Deploy from a local asset (`AssetPath`) or existing Lambda code such as an S3 object (`Code`)

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
            PolicyName = "my-api-policy",
            IncludeOtelLayer = true // Enable OpenTelemetry (disabled by default in v2.0+)
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
| `RoleName` | `string` | Name of the IAM role |
| `PolicyName` | `string` | Name of the IAM policy |

### Artifact Code (exactly one required)

Exactly one of `AssetPath` or `Code` must be set. Providing both or neither throws an `ArgumentException` at construction.

| Property | Type | Description |
|----------|------|-------------|
| `AssetPath` | `string?` | Path to a local Lambda deployment package (asset) |
| `Code` | `Code?` | Existing Lambda code, for example `Code.FromBucketV2(bucket, key, options)` |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AliasName` | `string` | `"live"` | Name of the alias pointing at the published version |
| `MemorySize` | `double` | `1024` | Memory allocation in MB |
| `TimeoutInSeconds` | `double` | `6` | Function timeout in seconds |
| `PolicyStatements` | `PolicyStatement[]` | `[]` | Additional IAM policy statements |
| `EnvironmentVariables` | `IDictionary<string, string>` | `{}` | Environment variables |
| `IncludeOtelLayer` | `bool` | `false` | Enable OpenTelemetry layer |
| `OtelLayerVersion` | `string` | `"0-117-0"` | OpenTelemetry layer version |
| `Architecture` | `string` | `"amd64"` | Lambda architecture applied to both the function and OTEL layer (amd64/arm64) |
| `Permissions` | `List<LambdaPermission>` | `[]` | Lambda invocation permissions, each with its own `Targets` |
| `EnableSnapStart` | `bool` | `false` | Enable SnapStart (Java runtimes only; the default `PROVIDED_AL2023` runtime is not eligible) |
| `GenerateUrl` | `bool` | `false` | Generate Function URL for HTTP access |

!!! warning "Breaking changes in 3.0.0"
    - Lambda permissions now default to the **alias only**; the function and version permission
      resources are no longer created unless `Targets` requests them.
    - The execution-role logging policy is scoped to the construct's own log group ARN. It no longer
      grants `logs:CreateLogGroup` or `logs:TagResource` and no longer uses a wildcard log-group resource.
    - `AssetPath` is no longer required: provide exactly one of `AssetPath` or `Code`.

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

By default a permission is granted on the **alias only**. Set `Targets` to grant it on the
function, the version, and/or the alias.

```csharp
var apiPermission = new LambdaPermission
{
    Principal = "apigateway.amazonaws.com",
    Action = "lambda:InvokeFunction",
    SourceArn = "arn:aws:execute-api:us-east-1:123456789012:abcdef123/*"
    // Targets defaults to LambdaPermissionTargets.Alias
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

To restore the 2.x behavior (function, version, and alias), opt in explicitly:

```csharp
var apiPermission = new LambdaPermission
{
    Principal = "apigateway.amazonaws.com",
    Action = "lambda:InvokeFunction",
    SourceArn = "arn:aws:execute-api:us-east-1:123456789012:abcdef123/*",
    Targets = LambdaPermissionTargets.Function
        | LambdaPermissionTargets.Version
        | LambdaPermissionTargets.Alias
};
```

### Lambda with an S3 Artifact

Provide existing code instead of a local asset. With `Code.FromBucketV2` the construct adds no
bucket grants to the execution role.

```csharp
var bucket = Bucket.FromBucketName(this, "ArtifactBucket", "my-lambda-artifacts");

var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    Code = Code.FromBucketV2(bucket, "my-api/app.zip", new BucketOptions()),
    RoleName = "my-api-role",
    PolicyName = "my-api-policy"
});
```

### Lambda with a Custom Alias Name

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    AliasName = "prod",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy"
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
    EnableSnapStart = true // SnapStart is only supported for Java runtimes
});
```

### Lambda with OpenTelemetry Configuration

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-deployment.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    IncludeOtelLayer = true,           // Enable OpenTelemetry layer
    Architecture = "arm64",            // Use ARM64 architecture
    OtelLayerVersion = "0-117-0"       // Specify OTEL layer version
});
```

!!! warning "Breaking Change in v2.0.0"
    Starting with version 2.0.0, the OpenTelemetry layer is **disabled by default**. You must explicitly set `IncludeOtelLayer = true` to enable it. This change allows for better control over observability costs and layer dependencies.

## Public Properties

### LambdaFunction
Access the underlying CDK Lambda function for advanced configuration:

```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", props);
var underlyingFunction = lambda.LambdaFunction;

// Add additional configuration
underlyingFunction.AddEnvironment("CUSTOM_VAR", "value");
```

### Alias

The deployed alias as an `IAlias`:

```csharp
var alias = lambda.Alias;
```

### Version

The published version backing the alias as an `IVersion`:

```csharp
var version = lambda.Version;
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

## :computer: Runtime Configuration

The Lambda functions use the following runtime configuration:

!!! info "Runtime Details"
    - **Runtime**: `PROVIDED_AL2023` (Amazon Linux 2023)
    - **Handler**: `bootstrap` (for custom runtimes)
    - **Architecture**: Configurable (amd64/arm64, default: amd64) and applied to both function and OTEL layer
    - **Log Retention**: 2 weeks
    - **OpenTelemetry Layer**: Configurable AWS managed layer (disabled by default in v2.0+)
    - **SnapStart**: Supported only for Java runtimes; enabling it with the default runtime is not allowed

## IAM Permissions

The construct automatically creates:

1. **CloudWatch Logs Permissions** (scoped to the construct's own log group ARN):
   - `logs:CreateLogStream`
   - `logs:PutLogEvents`

   The construct creates the log group itself, so it does not grant `logs:CreateLogGroup`,
   `logs:TagResource`, or a wildcard log-group resource.

2. **Custom Policy Statements**: Any additional policies you provide

3. **Lambda Permissions**: Each `LambdaPermission` is applied to its `Targets`
   (`Function`, `Version`, and/or `Alias`). The default is **alias only**. `LambdaPermissionTargets.None`
   is invalid and throws an `ArgumentException` at construction.

## Versioning Strategy

- Creates a new version on every deployment
- Maintains an alias pointing to the latest version; the alias name is configurable (`AliasName`, default `"live"`)
- Versions have `RemovalPolicy.RETAIN` to prevent deletion

## Testing

See the [Testing Guide](../testing/index.md) for comprehensive testing utilities and patterns specific to the Lambda Function construct.

## Examples

For more real-world examples, see the [Examples](../examples/index.md) section.

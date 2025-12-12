# DynamoDB Table Construct

The `DynamoDbTableConstruct` provides a comprehensive, production-ready DynamoDB table with support for global secondary indexes, streams, TTL, and Lambda integration.

## :bar_chart: Features

- **:key: Flexible Key Schema**: Support for partition keys, sort keys, and composite keys
- **:index_pointing_at_the_viewer: Global Secondary Indexes**: Multiple GSIs with custom key schemas
- **:ocean: DynamoDB Streams**: Real-time data processing with Lambda integration
- **:hourglass: TTL Support**: Automatic data expiration
- **:outbox_tray: CloudFormation Outputs**: Automatic exports for table ARN, name, and stream ARN
- **:zap: Lambda Integration**: Built-in method for attaching Lambda functions to streams
- **:credit_card: Billing Modes**: Support for both PAY_PER_REQUEST and PROVISIONED billing

## Basic Usage

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
    PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
}
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `TableName` | `string` | Name of the DynamoDB table |
| `PartitionKey` | `IAttribute` | Primary partition key definition |
| `RemovalPolicy` | `RemovalPolicy` | Behavior when the stack is deleted (e.g., `RemovalPolicy.DESTROY`) |
| `BillingMode` | `BillingMode` | Billing mode for the table (`PAY_PER_REQUEST` or `PROVISIONED`) |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SortKey` | `IAttribute?` | `null` | Primary sort key definition |
| `GlobalSecondaryIndexes` | `GlobalSecondaryIndexProps[]` | `[]` | GSI definitions |
| `Stream` | `StreamViewType?` | `null` | DynamoDB stream configuration |
| `TimeToLiveAttribute` | `string?` | `null` | TTL attribute name |

> PartitionKey is required. The construct validates this at creation time and will throw if it is missing to avoid synth/deploy failures.

## Advanced Examples

### Table with Sort Key

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "user-sessions",
    PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "sessionId", Type = AttributeType.STRING },
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Table with Global Secondary Index

```csharp
var gsi = new GlobalSecondaryIndexProps
{
    IndexName = "email-index",
    PartitionKey = new Attribute { Name = "email", Type = AttributeType.STRING },
    ProjectionType = ProjectionType.ALL
};

var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
    GlobalSecondaryIndexes = [gsi],
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Table with DynamoDB Streams

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new Attribute { Name = "eventId", Type = AttributeType.STRING },
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Table with TTL

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "sessions",
    PartitionKey = new Attribute { Name = "sessionId", Type = AttributeType.STRING },
    TimeToLiveAttribute = "expiresAt", // Unix timestamp field
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Complete Configuration

```csharp
var emailGsi = new GlobalSecondaryIndexProps
{
    IndexName = "email-index",
    PartitionKey = new Attribute { Name = "email", Type = AttributeType.STRING },
    ProjectionType = ProjectionType.ALL
};

var statusGsi = new GlobalSecondaryIndexProps
{
    IndexName = "status-index",
    PartitionKey = new Attribute { Name = "status", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "createdAt", Type = AttributeType.NUMBER },
    ProjectionType = ProjectionType.KEYS_ONLY
};

var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new Attribute { Name = "userId", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "createdAt", Type = AttributeType.NUMBER },
    GlobalSecondaryIndexes = [emailGsi, statusGsi],
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    TimeToLiveAttribute = "expiresAt",
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

## Lambda Stream Integration

The construct provides a convenient method to attach Lambda functions to DynamoDB streams:

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new Attribute { Name = "eventId", Type = AttributeType.STRING },
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});

// Create a Lambda function to process stream events
var streamProcessor = new LambdaFunctionConstruct(this, "StreamProcessor", new LambdaFunctionConstructProps
{
    FunctionName = "stream-processor",
    FunctionSuffix = "prod",
    AssetPath = "./lambda-stream-processor.zip",
    RoleName = "stream-processor-role",
    PolicyName = "stream-processor-policy"
});

// Attach Lambda to the stream
table.AttachStreamLambda(streamProcessor.LambdaFunction);
```

## CloudFormation Outputs

The construct automatically creates CloudFormation outputs:

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", props);

// Outputs created:
// - Export names follow: {stack-name}-{construct-id}-{qualifier} (all lowercase)
//   - Qualifiers: arn, name, stream-arn (stream only when enabled)
```

## Stream View Types

| View Type | Description |
|-----------|-------------|
| `KEYS_ONLY` | Only key attributes of the modified item |
| `NEW_IMAGE` | Entire item after modification |
| `OLD_IMAGE` | Entire item before modification |
| `NEW_AND_OLD_IMAGES` | Both new and old images of the item |

## Billing Modes

### PAY_PER_REQUEST (Default)
- No provisioned capacity required
- Scales automatically based on demand
- Pay only for actual read/write requests

### PROVISIONED
- Specify read and write capacity units
- More predictable costs for steady workloads
- Requires capacity planning

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    BillingMode = BillingMode.PROVISIONED,
    // Note: Provisioned capacity configuration would be added in future versions
});
```

## Global Secondary Index Configuration

### Projection Types

| Projection Type | Description |
|----------------|-------------|
| `ALL` | All attributes projected |
| `KEYS_ONLY` | Only key attributes projected |
| `INCLUDE` | Key attributes plus specified attributes |

### GSI Example with Projection

```csharp
var gsi = new GlobalSecondaryIndexProps
{
    IndexName = "status-index",
    PartitionKey = new Attribute { Name = "status", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "updatedAt", Type = AttributeType.NUMBER },
    ProjectionType = ProjectionType.INCLUDE,
    NonKeyAttributes = ["name", "email"] // Only with INCLUDE projection
};
```

## Common Use Cases

### User Management Table
```csharp
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
```

### Session Management Table
```csharp
var sessionTable = new DynamoDbTableConstruct(this, "SessionTable", new DynamoDbTableConstructProps
{
    TableName = "sessions",
    PartitionKey = new Attribute { Name = "sessionId", Type = AttributeType.STRING },
    TimeToLiveAttribute = "expiresAt",
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

### Event Sourcing Table
```csharp
var eventTable = new DynamoDbTableConstruct(this, "EventTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new Attribute { Name = "aggregateId", Type = AttributeType.STRING },
    SortKey = new Attribute { Name = "timestamp", Type = AttributeType.NUMBER },
    Stream = StreamViewType.NEW_AND_OLD_IMAGES,
    RemovalPolicy = RemovalPolicy.DESTROY,
    BillingMode = BillingMode.PAY_PER_REQUEST
});
```

## Testing

See the [Testing Guide](../testing/index.md) for comprehensive testing utilities and patterns specific to the DynamoDB Table construct.

## Examples

For more real-world examples, see the [Examples](../examples/index.md) section.

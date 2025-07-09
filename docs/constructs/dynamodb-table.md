---
layout: default
title: DynamoDB Table Construct
---

# DynamoDB Table Construct

The `DynamoDbTableConstruct` provides a comprehensive, production-ready DynamoDB table with support for global secondary indexes, streams, TTL, and Lambda integration.

## Features

- **Flexible Key Schema**: Support for partition keys, sort keys, and composite keys
- **Global Secondary Indexes**: Multiple GSIs with custom key schemas
- **DynamoDB Streams**: Real-time data processing with Lambda integration
- **TTL Support**: Automatic data expiration
- **CloudFormation Outputs**: Automatic exports for table ARN, name, and stream ARN
- **Lambda Integration**: Built-in method for attaching Lambda functions to streams
- **Billing Modes**: Support for both PAY_PER_REQUEST and PROVISIONED billing

## Basic Usage

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
        {
            TableName = "users",
            PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING }
        });
    }
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `TableName` | `string` | Name of the DynamoDB table |
| `PartitionKey` | `AttributeDefinition` | Primary partition key definition |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SortKey` | `AttributeDefinition?` | `null` | Primary sort key definition |
| `GlobalSecondaryIndexes` | `GlobalSecondaryIndex[]` | `[]` | GSI definitions |
| `StreamSpecification` | `StreamViewType?` | `null` | DynamoDB stream configuration |
| `TimeToLiveAttribute` | `string?` | `null` | TTL attribute name |
| `BillingMode` | `BillingMode` | `PAY_PER_REQUEST` | Table billing mode |

## Advanced Examples

### Table with Sort Key

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "user-sessions",
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "sessionId", AttributeType = AttributeType.STRING }
});
```

### Table with Global Secondary Index

```csharp
var gsi = new GlobalSecondaryIndex
{
    IndexName = "email-index",
    PartitionKey = new AttributeDefinition { AttributeName = "email", AttributeType = AttributeType.STRING },
    ProjectionType = ProjectionType.ALL
};

var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    GlobalSecondaryIndexes = [gsi]
});
```

### Table with DynamoDB Streams

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new AttributeDefinition { AttributeName = "eventId", AttributeType = AttributeType.STRING },
    StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES
});
```

### Table with TTL

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "sessions",
    PartitionKey = new AttributeDefinition { AttributeName = "sessionId", AttributeType = AttributeType.STRING },
    TimeToLiveAttribute = "expiresAt" // Unix timestamp field
});
```

### Complete Configuration

```csharp
var emailGsi = new GlobalSecondaryIndex
{
    IndexName = "email-index",
    PartitionKey = new AttributeDefinition { AttributeName = "email", AttributeType = AttributeType.STRING },
    ProjectionType = ProjectionType.ALL
};

var statusGsi = new GlobalSecondaryIndex
{
    IndexName = "status-index",
    PartitionKey = new AttributeDefinition { AttributeName = "status", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "createdAt", AttributeType = AttributeType.NUMBER },
    ProjectionType = ProjectionType.KEYS_ONLY
};

var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "users",
    PartitionKey = new AttributeDefinition { AttributeName = "userId", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "createdAt", AttributeType = AttributeType.NUMBER },
    GlobalSecondaryIndexes = [emailGsi, statusGsi],
    StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES,
    TimeToLiveAttribute = "expiresAt"
});
```

## Lambda Stream Integration

The construct provides a convenient method to attach Lambda functions to DynamoDB streams:

```csharp
var table = new DynamoDbTableConstruct(this, "MyTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new AttributeDefinition { AttributeName = "eventId", AttributeType = AttributeType.STRING },
    StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES
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
// - {StackName}-MyTable-table-arn-output
// - {StackName}-MyTable-table-name-output
// - {StackName}-MyTable-table-stream-arn-output (if streams enabled)
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
var gsi = new GlobalSecondaryIndex
{
    IndexName = "status-index",
    PartitionKey = new AttributeDefinition { AttributeName = "status", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "updatedAt", AttributeType = AttributeType.NUMBER },
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

### Session Management Table
```csharp
var sessionTable = new DynamoDbTableConstruct(this, "SessionTable", new DynamoDbTableConstructProps
{
    TableName = "sessions",
    PartitionKey = new AttributeDefinition { AttributeName = "sessionId", AttributeType = AttributeType.STRING },
    TimeToLiveAttribute = "expiresAt"
});
```

### Event Sourcing Table
```csharp
var eventTable = new DynamoDbTableConstruct(this, "EventTable", new DynamoDbTableConstructProps
{
    TableName = "events",
    PartitionKey = new AttributeDefinition { AttributeName = "aggregateId", AttributeType = AttributeType.STRING },
    SortKey = new AttributeDefinition { AttributeName = "timestamp", AttributeType = AttributeType.NUMBER },
    StreamSpecification = StreamViewType.NEW_AND_OLD_IMAGES
});
```

## Testing

See the [Testing Guide](../testing/) for comprehensive testing utilities and patterns specific to the DynamoDB Table construct.

## Examples

For more real-world examples, see the [Examples](../examples/) section.
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using LayeredCraft.Cdk.Constructs.Models;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for creating DynamoDbTableConstructProps instances in tests.
/// Provides sensible defaults and allows customization of specific properties.
/// </summary>
public class DynamoDbTableConstructPropsBuilder
{
    private string _tableName = "test-table";
    private IAttribute? _partitionKey;
    private IAttribute? _sortKey;
    private RemovalPolicy _removalPolicy = RemovalPolicy.DESTROY;
    private BillingMode _billingMode = BillingMode.PAY_PER_REQUEST;
    private readonly List<GlobalSecondaryIndexProps> _globalSecondaryIndexes = new();
    private StreamViewType? _stream;
    private string? _timeToLiveAttribute;

    /// <summary>
    /// Sets the table name.
    /// </summary>
    /// <param name="tableName">The table name</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithTableName(string tableName)
    {
        _tableName = tableName;
        return this;
    }

    /// <summary>
    /// Sets the partition key for the table.
    /// </summary>
    /// <param name="keyName">The partition key name</param>
    /// <param name="keyType">The partition key type</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithPartitionKey(string keyName, AttributeType keyType = AttributeType.STRING)
    {
        _partitionKey = new Attribute
        {
            Name = keyName,
            Type = keyType
        };
        return this;
    }

    /// <summary>
    /// Sets the sort key for the table.
    /// </summary>
    /// <param name="keyName">The sort key name</param>
    /// <param name="keyType">The sort key type</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithSortKey(string keyName, AttributeType keyType = AttributeType.STRING)
    {
        _sortKey = new Attribute
        {
            Name = keyName,
            Type = keyType
        };
        return this;
    }

    /// <summary>
    /// Sets the removal policy for the table.
    /// </summary>
    /// <param name="removalPolicy">The removal policy</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithRemovalPolicy(RemovalPolicy removalPolicy)
    {
        _removalPolicy = removalPolicy;
        return this;
    }

    /// <summary>
    /// Sets the billing mode for the table.
    /// </summary>
    /// <param name="billingMode">The billing mode</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithBillingMode(BillingMode billingMode)
    {
        _billingMode = billingMode;
        return this;
    }

    /// <summary>
    /// Adds a global secondary index to the table.
    /// </summary>
    /// <param name="indexName">The GSI name</param>
    /// <param name="partitionKeyName">The GSI partition key name</param>
    /// <param name="partitionKeyType">The GSI partition key type</param>
    /// <param name="sortKeyName">The GSI sort key name (optional)</param>
    /// <param name="sortKeyType">The GSI sort key type</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithGlobalSecondaryIndex(
        string indexName, 
        string partitionKeyName, 
        AttributeType partitionKeyType = AttributeType.STRING,
        string? sortKeyName = null,
        AttributeType sortKeyType = AttributeType.STRING)
    {
        var gsiProps = new GlobalSecondaryIndexProps
        {
            IndexName = indexName,
            PartitionKey = new Attribute
            {
                Name = partitionKeyName,
                Type = partitionKeyType
            }
        };

        if (sortKeyName != null)
        {
            gsiProps.SortKey = new Attribute
            {
                Name = sortKeyName,
                Type = sortKeyType
            };
        }

        _globalSecondaryIndexes.Add(gsiProps);
        return this;
    }

    /// <summary>
    /// Enables DynamoDB streams with the specified view type.
    /// </summary>
    /// <param name="streamViewType">The stream view type</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithStream(StreamViewType streamViewType)
    {
        _stream = streamViewType;
        return this;
    }

    /// <summary>
    /// Disables DynamoDB streams.
    /// </summary>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithoutStream()
    {
        _stream = null;
        return this;
    }

    /// <summary>
    /// Sets the time-to-live attribute for the table.
    /// </summary>
    /// <param name="attributeName">The TTL attribute name</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithTimeToLiveAttribute(string attributeName)
    {
        _timeToLiveAttribute = attributeName;
        return this;
    }

    /// <summary>
    /// Removes the time-to-live attribute from the table.
    /// </summary>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder WithoutTimeToLiveAttribute()
    {
        _timeToLiveAttribute = null;
        return this;
    }

    /// <summary>
    /// Configures the table for a typical user data scenario.
    /// Sets up partition key "userId", sort key "itemId", and a GSI for querying by item type.
    /// </summary>
    /// <param name="tableName">The table name</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder ForUserTable(string tableName = "users")
    {
        return WithTableName(tableName)
            .WithPartitionKey("userId")
            .WithSortKey("itemId")
            .WithGlobalSecondaryIndex("GSI1", "itemType", AttributeType.STRING, "createdAt", AttributeType.STRING);
    }

    /// <summary>
    /// Configures the table for a typical session management scenario.
    /// Sets up partition key "sessionId", TTL attribute, and streams for session tracking.
    /// </summary>
    /// <param name="tableName">The table name</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder ForSessionTable(string tableName = "sessions")
    {
        return WithTableName(tableName)
            .WithPartitionKey("sessionId")
            .WithTimeToLiveAttribute("expiresAt")
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES);
    }

    /// <summary>
    /// Configures the table for event sourcing scenarios.
    /// Sets up partition key "aggregateId", sort key "eventId", and streams for event processing.
    /// </summary>
    /// <param name="tableName">The table name</param>
    /// <returns>The builder instance for method chaining</returns>
    public DynamoDbTableConstructPropsBuilder ForEventSourcing(string tableName = "events")
    {
        return WithTableName(tableName)
            .WithPartitionKey("aggregateId")
            .WithSortKey("eventId")
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
            .WithGlobalSecondaryIndex("GSI1", "eventType", AttributeType.STRING, "timestamp", AttributeType.NUMBER);
    }

    /// <summary>
    /// Builds the DynamoDbTableConstructProps instance.
    /// </summary>
    /// <returns>The configured DynamoDbTableConstructProps</returns>
    public DynamoDbTableConstructProps Build()
    {
        return new DynamoDbTableConstructProps
        {
            TableName = _tableName,
            PartitionKey = _partitionKey,
            SortKey = _sortKey,
            RemovalPolicy = _removalPolicy,
            BillingMode = _billingMode,
            GlobalSecondaryIndexes = [.. _globalSecondaryIndexes],
            Stream = _stream,
            TimeToLiveAttribute = _timeToLiveAttribute
        };
    }
}
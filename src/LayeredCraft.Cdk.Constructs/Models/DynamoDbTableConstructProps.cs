using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Configuration interface for DynamoDB table construct properties.
/// Defines the contract for configuring DynamoDB tables with partition keys, sort keys,
/// global secondary indexes, streams, and time-to-live attributes.
/// </summary>
public interface IDynamoDbTableConstructProps
{
    /// <summary>
    /// The name of the DynamoDB table.
    /// </summary>
    public string TableName { get; set; }
    
    /// <summary>
    /// The partition key attribute for the table.
    /// </summary>
    public IAttribute? PartitionKey { get; set; }
    
    /// <summary>
    /// The sort key attribute for the table.
    /// </summary>
    public IAttribute? SortKey { get; set; }
    
    /// <summary>
    /// The removal policy for the table when the stack is deleted.
    /// </summary>
    public RemovalPolicy RemovalPolicy { get; set; }
    
    /// <summary>
    /// The billing mode for the table (PAY_PER_REQUEST or PROVISIONED).
    /// </summary>
    public BillingMode BillingMode { get; set; }
    
    /// <summary>
    /// Array of global secondary index configurations for the table.
    /// </summary>
    public GlobalSecondaryIndexProps[] GlobalSecondaryIndexes { get; set; }
    
    /// <summary>
    /// The stream view type for DynamoDB streams, if enabled.
    /// </summary>
    StreamViewType? Stream { get; set; }
    
    /// <summary>
    /// The attribute name for time-to-live (TTL) functionality.
    /// </summary>
    string? TimeToLiveAttribute { get; set; }
}

/// <summary>
/// Configuration record for DynamoDB table construct properties.
/// Provides immutable configuration for creating DynamoDB tables with comprehensive options
/// including partition keys, sort keys, global secondary indexes, streams, and TTL.
/// </summary>
public record DynamoDbTableConstructProps : IDynamoDbTableConstructProps
{
    /// <summary>
    /// The name of the DynamoDB table.
    /// </summary>
    public required string TableName { get; set; }
    
    /// <summary>
    /// The partition key attribute for the table.
    /// </summary>
    public IAttribute? PartitionKey { get; set; }
    
    /// <summary>
    /// The sort key attribute for the table.
    /// </summary>
    public IAttribute? SortKey { get; set; }
    
    /// <summary>
    /// The removal policy for the table when the stack is deleted.
    /// </summary>
    public required RemovalPolicy RemovalPolicy { get; set; }
    
    /// <summary>
    /// The billing mode for the table (PAY_PER_REQUEST or PROVISIONED).
    /// </summary>
    public required BillingMode BillingMode { get; set; }
    
    /// <summary>
    /// Array of global secondary index configurations for the table.
    /// </summary>
    public GlobalSecondaryIndexProps[] GlobalSecondaryIndexes { get; set; } = [];
    
    /// <summary>
    /// The stream view type for DynamoDB streams, if enabled.
    /// </summary>
    public StreamViewType? Stream { get; set; }
    
    /// <summary>
    /// The attribute name for time-to-live (TTL) functionality.
    /// </summary>
    public string? TimeToLiveAttribute { get; set; }
}
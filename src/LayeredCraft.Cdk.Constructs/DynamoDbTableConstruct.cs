using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Constructs;
using LayeredCraft.Cdk.Constructs.Extensions;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// AWS CDK construct for creating DynamoDB tables with comprehensive configuration.
/// This construct creates a DynamoDB table with support for global secondary indexes, streams,
/// time-to-live attributes, and automatically generates CloudFormation outputs for easy reference.
/// </summary>
public class DynamoDbTableConstruct : Construct
{
    /// <summary>
    /// The ARN of the created DynamoDB table.
    /// </summary>
    public readonly string TableArn;
    
    /// <summary>
    /// The name of the created DynamoDB table.
    /// </summary>
    public readonly string TableName;
    
    /// <summary>
    /// The ARN of the DynamoDB table stream, if streams are enabled.
    /// </summary>
    public readonly string? TableStreamArn;
    
    /// <summary>
    /// The underlying CDK Table construct for advanced configuration.
    /// </summary>
    public readonly Table Table;
    /// <summary>
    /// Initializes a new instance of the DynamoDbTableConstruct.
    /// </summary>
    /// <param name="scope">The scope in which to define this construct</param>
    /// <param name="id">The scoped construct ID</param>
    /// <param name="props">The configuration properties for the DynamoDB table</param>
    public DynamoDbTableConstruct(Construct scope, string id, IDynamoDbTableConstructProps props) : base(scope, id)
    {
        var tableProps = new TableProps
        {
            TableName = props.TableName,
            RemovalPolicy = props.RemovalPolicy,
            BillingMode = props.BillingMode
        };
        if (props.PartitionKey != null)
            tableProps.PartitionKey = props.PartitionKey;
        if (props.SortKey != null)
            tableProps.SortKey = props.SortKey;
        if (props.Stream.HasValue)
            tableProps.Stream = props.Stream.Value;
        if (!string.IsNullOrEmpty(props.TimeToLiveAttribute))
        {
            tableProps.TimeToLiveAttribute = props.TimeToLiveAttribute;
        }
        
        Table = new Table(this, id, tableProps);

        var stack = Stack.Of(this);
        for (var i = 0; i < props.GlobalSecondaryIndexes.Length; i++)
        {
            var index = props.GlobalSecondaryIndexes[i];
            Table.AddGlobalSecondaryIndex(index);
            _ = new CfnOutput(this, $"{id}-gsi-{i}", new CfnOutputProps
            {
                ExportName = stack.CreateExportName(id, $"gsi-{i}"),
                Value = index.IndexName
            });
        }
        
        TableArn = Table.TableArn;
        TableName = props.TableName;
        TableStreamArn = Table.TableStreamArn;
        
        _ = new CfnOutput(this, $"{id}-arn", new CfnOutputProps
        {
            ExportName = stack.CreateExportName(id, "arn"),
            Value = TableArn
        });

        _ = new CfnOutput(this, $"{id}-name", new CfnOutputProps
        {
            ExportName = stack.CreateExportName(id, "name"),
            Value = TableName
        });

        if (TableStreamArn is not null)
        {
            _ = new CfnOutput(this, $"{id}-stream-arn", new CfnOutputProps
            {
                ExportName = stack.CreateExportName(id, "stream-arn"),
                Value = TableStreamArn
            });
        }
    }
    /// <summary>
    /// Attaches a Lambda function to the DynamoDB table stream for processing stream records.
    /// </summary>
    /// <param name="lambda">The Lambda function to attach to the stream</param>
    /// <exception cref="InvalidOperationException">Thrown when the table doesn't have streams enabled</exception>
    public void AttachStreamLambda(Function lambda)
    {
        if (TableStreamArn is null)
            throw new InvalidOperationException("Cannot attach stream Lambda to table without streams enabled");
            
        _ = new EventSourceMapping(this, $"{TableName}-stream-mapping", new EventSourceMappingProps
        {
            Target = lambda,
            EventSourceArn = TableStreamArn,
            StartingPosition = StartingPosition.TRIM_HORIZON,
            BatchSize = 1
        });
    }
}
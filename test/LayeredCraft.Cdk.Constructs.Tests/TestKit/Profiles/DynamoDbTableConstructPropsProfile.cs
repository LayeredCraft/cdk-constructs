using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Supplies the deterministic, realistic <see cref="DynamoDbTableConstructProps"/> test data the DynamoDB
/// table construct tests rely on. Every member was pinned by the previous customization (the optional ones
/// to a fixed value, including null/empty), so every member is pinned here too.
/// </summary>
public sealed class DynamoDbTableConstructPropsProfile : ICompositionProfile
{
    // Compono does not provide this because composition populates constructor parameters and required
    // members only; the behavior-defining PartitionKey member is an optional (non-required) property, so a
    // registration factory supplies the whole value.
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            SortKey = null,
            GlobalSecondaryIndexes = [],
            Stream = null,
            TimeToLiveAttribute = null,
        });
}

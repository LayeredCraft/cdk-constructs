using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Composes the deterministic, realistic <see cref="DynamoDbTableConstructProps"/> test data the
/// DynamoDB table construct tests rely on.
/// </summary>
public sealed class DynamoDbTableConstructPropsProfile : ICompositionProfile
{
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
        });
}

using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using LayeredCraft.Cdk.Constructs.Models;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

public class DynamoDbTableConstructCustomization(bool includeGsi = false, bool includeStream = false, bool includeTtl = false) : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Customize<DynamoDbTableConstructProps>(transform => transform
            .With(props => props.TableName, "test-table")
            .With(props => props.RemovalPolicy, RemovalPolicy.DESTROY)
            .With(props => props.BillingMode, BillingMode.PAY_PER_REQUEST)
            .With(props => props.PartitionKey, new Attribute { Name = "pk", Type = AttributeType.STRING })
            .With(props => props.SortKey, includeGsi ? 
                new Attribute { Name = "sk", Type = AttributeType.STRING } : 
                null)
            .With(props => props.GlobalSecondaryIndexes, includeGsi ? 
                [
                    new()
                    {
                        IndexName = "GSI1",
                        PartitionKey = new Attribute { Name = "gsi1pk", Type = AttributeType.STRING },
                        SortKey = new Attribute { Name = "gsi1sk", Type = AttributeType.STRING }
                    }
                ] : 
                [])
            .With(props => props.Stream, includeStream ? 
                StreamViewType.NEW_AND_OLD_IMAGES : 
                null)
            .With(props => props.TimeToLiveAttribute, includeTtl ? 
                "ttl" : 
                null));
    }
}
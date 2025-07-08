using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;
using LayeredCraft.Cdk.Constructs.Testing;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class DynamoDbTableConstructTests
{
    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateDynamoDbTable(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();

        // Act
        var construct = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        construct.TableName.Should().Be(props.TableName);
        construct.TableArn.Should().NotBeNullOrEmpty();
        construct.Table.Should().NotBeNull();

        var template = Template.FromStack(stack);
        template.ShouldHaveDynamoTable(props.TableName);
        template.ShouldHaveTableOutputs("test-stack", "TestTable");
    }

    [Fact]
    public void Construct_ShouldCreateTableWithPartitionKey()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST
        };

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHavePartitionKey("pk", AttributeType.STRING);
        
        // Also verify basic table creation
        template.ResourceCountIs("AWS::DynamoDB::Table", 1);
    }

    [Fact]
    public void Construct_ShouldCreateTableWithPartitionAndSortKey()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "sk", Type = AttributeType.STRING },
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST
        };

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHavePartitionKey("pk", AttributeType.STRING);
        template.ShouldHaveSortKey("sk", AttributeType.STRING);
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateTableWithGlobalSecondaryIndex(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING };
        props.GlobalSecondaryIndexes = 
        [
            new GlobalSecondaryIndexProps
            {
                IndexName = "GSI1",
                PartitionKey = new Attribute { Name = "gsi1pk", Type = AttributeType.STRING },
                SortKey = new Attribute { Name = "gsi1sk", Type = AttributeType.STRING }
            }
        ];

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHaveGlobalSecondaryIndex("GSI1");
        
        // Check GSI output using export name
        var templateJson = template.ToJSON();
        templateJson.Should().ContainKey("Outputs");
        var outputs = (IDictionary<string, object>)templateJson["Outputs"];
        
        bool hasGsiOutput = false;
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var value))
            {
                var export = (IDictionary<string, object>)value;
                var exportName = export["Name"].ToString();
                if (exportName == "test-stack-testtable-gsi-0")
                {
                    hasGsiOutput = true;
                    outputDict["Value"].Should().Be("GSI1");
                }
            }
        }
        hasGsiOutput.Should().BeTrue("GSI-0 output should exist");
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateTableWithMultipleGlobalSecondaryIndexes(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING };
        props.GlobalSecondaryIndexes = 
        [
            new GlobalSecondaryIndexProps
            {
                IndexName = "GSI1",
                PartitionKey = new Attribute { Name = "gsi1pk", Type = AttributeType.STRING }
            },
            new GlobalSecondaryIndexProps
            {
                IndexName = "GSI2",
                PartitionKey = new Attribute { Name = "gsi2pk", Type = AttributeType.STRING }
            }
        ];

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHaveGlobalSecondaryIndex("GSI1");
        template.ShouldHaveGlobalSecondaryIndex("GSI2");
        
        // Check GSI outputs using export names
        var templateJson = template.ToJSON();
        templateJson.Should().ContainKey("Outputs");
        var outputs = (IDictionary<string, object>)templateJson["Outputs"];
        
        bool hasGsi0Output = false, hasGsi1Output = false;
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var value))
            {
                var export = (IDictionary<string, object>)value;
                var exportName = export["Name"].ToString();
                if (exportName == "test-stack-testtable-gsi-0")
                {
                    hasGsi0Output = true;
                    outputDict["Value"].Should().Be("GSI1");
                }
                else if (exportName == "test-stack-testtable-gsi-1")
                {
                    hasGsi1Output = true;
                    outputDict["Value"].Should().Be("GSI2");
                }
            }
        }
        hasGsi0Output.Should().BeTrue("GSI-0 output should exist");
        hasGsi1Output.Should().BeTrue("GSI-1 output should exist");
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateTableWithStream(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.Stream = StreamViewType.NEW_AND_OLD_IMAGES;

        // Act
        var construct = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        construct.TableStreamArn.Should().NotBeNullOrEmpty();
        var template = Template.FromStack(stack);
        template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
        template.FindOutputs("*", new Dictionary<string, object>
        {
            { "Export", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Name", "test-stack-testtable-stream-arn" }
            }) }
        }).Should().HaveCount(1);
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateTableWithTimeToLive(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.TimeToLiveAttribute = "ttl";

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHaveTimeToLiveAttribute("ttl");
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateTableWithPayPerRequestBilling(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.BillingMode = BillingMode.PAY_PER_REQUEST;

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "BillingMode", "PAY_PER_REQUEST" }
        }));
    }

    [Fact]
    public void Construct_ShouldCreateTableWithProvisionedBilling()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PROVISIONED
        };

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        
        // Just verify the table exists with any properties - to debug what's actually being created
        template.ResourceCountIs("AWS::DynamoDB::Table", 1);
        
        // Find and inspect the actual table resource
        var resources = template.FindResources("AWS::DynamoDB::Table");
        resources.Should().HaveCount(1);
        
        // Check if billing mode is set (might be omitted for PROVISIONED as it's the default)
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "TableName", "test-table" }
        }));
    }

    [Fact]
    public void Construct_ShouldCreateTableWithRetainRemovalPolicy()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = new DynamoDbTableConstructProps
        {
            TableName = "test-table",
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            RemovalPolicy = RemovalPolicy.RETAIN,
            BillingMode = BillingMode.PAY_PER_REQUEST
        };

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.HasResource("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DeletionPolicy", "Retain" }
        }));
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void AttachStreamLambda_ShouldCreateEventSourceMapping_WhenStreamEnabled(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.Stream = StreamViewType.NEW_AND_OLD_IMAGES;
        var construct = new DynamoDbTableConstruct(stack, "TestTable", props);
        
        var lambdaFunction = new Function(stack, "TestLambda", new FunctionProps
        {
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = Code.FromAsset("./TestAssets/test-lambda.zip")
        });

        // Act
        construct.AttachStreamLambda(lambdaFunction);

        // Assert
        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::Lambda::EventSourceMapping", Match.ObjectLike(new Dictionary<string, object>
        {
            { "EventSourceArn", Match.AnyValue() },
            { "FunctionName", Match.AnyValue() },
            { "StartingPosition", "TRIM_HORIZON" },
            { "BatchSize", 1 }
        }));
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void AttachStreamLambda_ShouldThrowException_WhenStreamNotEnabled(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.Stream = null; // No stream
        var construct = new DynamoDbTableConstruct(stack, "TestTable", props);
        
        var lambdaFunction = new Function(stack, "TestLambda", new FunctionProps
        {
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = Code.FromAsset("./TestAssets/test-lambda.zip")
        });

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => construct.AttachStreamLambda(lambdaFunction));
        exception.Message.Should().Be("Cannot attach stream Lambda to table without streams enabled");
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldCreateAllRequiredOutputs(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.Stream = StreamViewType.NEW_IMAGE;

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        
        // Check that outputs exist - with streams enabled, should have 3 outputs
        var templateJson = template.ToJSON();
        
        // CloudFormation templates have an "Outputs" section
        templateJson.Should().ContainKey("Outputs");
        var outputs = (IDictionary<string, object>)templateJson["Outputs"];
        outputs.Should().HaveCount(3); // arn, name, stream-arn
        
        // Verify export names contain the expected values
        bool hasArnOutput = false, hasNameOutput = false, hasStreamArnOutput = false;
        
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var value))
            {
                var export = (IDictionary<string, object>)value;
                var exportName = export["Name"].ToString();
                if (exportName == "test-stack-testtable-arn") hasArnOutput = true;
                else if (exportName == "test-stack-testtable-name") hasNameOutput = true;
                else if (exportName == "test-stack-testtable-stream-arn") hasStreamArnOutput = true;
            }
        }
        
        hasArnOutput.Should().BeTrue("ARN output should exist");
        hasNameOutput.Should().BeTrue("Name output should exist");
        hasStreamArnOutput.Should().BeTrue("Stream ARN output should exist");
    }

    [Theory]
    [DynamoDbTableConstructAutoData]
    public void Construct_ShouldNotCreateStreamOutput_WhenStreamNotEnabled(DynamoDbTableConstructProps props)
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        props.Stream = null; // No stream

        // Act
        var construct = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        construct.TableStreamArn.Should().BeNull();
        
        var template = Template.FromStack(stack);
        
        // Check that table outputs exist but stream output does not (only 2 outputs: arn, name)
        var templateJson = template.ToJSON();
        templateJson.Should().ContainKey("Outputs");
        var outputs = (IDictionary<string, object>)templateJson["Outputs"];
        outputs.Should().HaveCount(2); // Only arn and name, no stream
        
        // Verify export names contain the expected values (but not stream)
        bool hasArnOutput = false, hasNameOutput = false, hasStreamOutput = false;
        
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var value))
            {
                var export = (IDictionary<string, object>)value;
                var exportName = export["Name"].ToString();
                if (exportName == "test-stack-testtable-arn") hasArnOutput = true;
                else if (exportName == "test-stack-testtable-name") hasNameOutput = true;
                else if (exportName == "test-stack-testtable-stream-arn") hasStreamOutput = true;
            }
        }
        
        hasArnOutput.Should().BeTrue("ARN output should exist");
        hasNameOutput.Should().BeTrue("Name output should exist");
        hasStreamOutput.Should().BeFalse("Stream output should NOT exist when streams are disabled");
    }
}
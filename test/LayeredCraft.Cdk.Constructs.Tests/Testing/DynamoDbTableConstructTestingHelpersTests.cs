using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.DynamoDB;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.Testing;

[Collection("CDK Tests")]
public class DynamoDbTableConstructTestingHelpersTests
{
    #region DynamoDbTableConstructAssertions Tests

    [Fact]
    public void ShouldHaveDynamoTable_ShouldSucceed_WhenTableExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithTableName("test-table")
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveDynamoTable("test-table");
    }

    [Fact]
    public void ShouldHavePartitionKey_ShouldSucceed_WhenPartitionKeyExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithPartitionKey("userId", AttributeType.STRING)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHavePartitionKey("userId", AttributeType.STRING);
    }

    [Fact]
    public void ShouldHavePartitionKey_ShouldSucceed_WithNumberType()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithPartitionKey("numericId", AttributeType.NUMBER)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHavePartitionKey("numericId", AttributeType.NUMBER);
    }

    [Fact]
    public void ShouldHavePartitionKey_ShouldSucceed_WithBinaryType()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithPartitionKey("binaryId", AttributeType.BINARY)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHavePartitionKey("binaryId", AttributeType.BINARY);
    }

    [Fact]
    public void ShouldHaveSortKey_ShouldSucceed_WhenSortKeyExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithSortKey("itemId", AttributeType.STRING)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveSortKey("itemId", AttributeType.STRING);
    }

    [Fact]
    public void ShouldHaveSortKey_ShouldSucceed_WithNumberType()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithSortKey("timestamp", AttributeType.NUMBER)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveSortKey("timestamp", AttributeType.NUMBER);
    }

    [Fact]
    public void ShouldHaveSortKey_ShouldSucceed_WithBinaryType()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithSortKey("binarySort", AttributeType.BINARY)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveSortKey("binarySort", AttributeType.BINARY);
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndex_ShouldSucceed_WhenGsiExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK", AttributeType.STRING)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveGlobalSecondaryIndex("GSI1");
    }

    [Fact]
    public void ShouldHaveTableStream_ShouldSucceed_WhenStreamExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
    }

    [Fact]
    public void ShouldHaveTableStream_ShouldSucceed_WithDifferentStreamTypes()
    {
        // Test all stream types
        var streamTypes = new[]
        {
            StreamViewType.NEW_IMAGE,
            StreamViewType.OLD_IMAGE,
            StreamViewType.NEW_AND_OLD_IMAGES,
            StreamViewType.KEYS_ONLY
        };

        foreach (var streamType in streamTypes)
        {
            // Arrange
            var stack = CdkTestHelper.CreateTestStackMinimal();
            var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
                .WithStream(streamType)
                .Build();
            
            _ = new DynamoDbTableConstruct(stack, "TestTable", props);
            var template = Template.FromStack(stack);

            // Act & Assert
            template.ShouldHaveTableStream(streamType);
        }
    }

    [Fact]
    public void ShouldHaveTimeToLiveAttribute_ShouldSucceed_WhenTtlExists()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithTimeToLiveAttribute("expiresAt")
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveTimeToLiveAttribute("expiresAt");
    }

    [Fact]
    public void ShouldHaveTableOutputs_ShouldSucceed_WhenOutputsExist()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal("test-stack");
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveTableOutputs("test-stack", "TestTable");
    }

    [Fact]
    public void ShouldHaveTableOutputs_ShouldNormalizeCasing()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal("TEST-STACK");
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveTableOutputs("TEST-STACK", "TestTable");
    }

    [Fact]
    public void ShouldHaveTableOutputs_ShouldThrow_WhenNoOutputsExist()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var template = Template.FromStack(stack);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            template.ShouldHaveTableOutputs("test-stack", "TestTable"));
        
        exception.Message.Should().Be("Template does not contain any outputs");
    }

    [Fact]
    public void ShouldHaveTableOutputs_ShouldThrow_WhenArnOutputMissing()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            template.ShouldHaveTableOutputs("wrong-stack", "TestTable"));
        
        exception.Message.Should().Contain("ARN output with export name 'wrong-stack-testtable-arn' should exist");
    }

    [Fact]
    public void ShouldHaveTableOutputs_ShouldThrow_WhenConstructIdMismatch()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            template.ShouldHaveTableOutputs("test-stack", "WrongTable"));
        
        // The method checks ARN first, so it will throw an ARN error when construct ID doesn't match
        exception.Message.Should().Contain("ARN output with export name 'test-stack-wrongtable-arn' should exist");
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndexCount_ShouldSucceed_WithZeroGsis()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveGlobalSecondaryIndexCount(0);
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndexCount_ShouldSucceed_WithOneGsi()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK")
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveGlobalSecondaryIndexCount(1);
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndexCount_ShouldSucceed_WithMultipleGsis()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK1")
            .WithGlobalSecondaryIndex("GSI2", "gsiPK2")
            .WithGlobalSecondaryIndex("GSI3", "gsiPK3")
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        template.ShouldHaveGlobalSecondaryIndexCount(3);
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndexCount_ShouldThrow_WhenCountMismatch()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK")
            .Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            template.ShouldHaveGlobalSecondaryIndexCount(2));
        
        exception.Message.Should().Be("Expected 2 Global Secondary Indexes but found 1");
    }

    [Fact]
    public void ShouldHaveGlobalSecondaryIndexCount_ShouldThrow_WhenExpectingGsisButNoneFound()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = CdkTestHelper.CreateDynamoDbTablePropsBuilder().Build();
        
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);
        var template = Template.FromStack(stack);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            template.ShouldHaveGlobalSecondaryIndexCount(1));
        
        exception.Message.Should().Be("Expected 1 Global Secondary Indexes but found none");
    }

    #endregion

    #region DynamoDbTableConstructPropsBuilder Tests

    [Fact]
    public void DynamoDbTableConstructPropsBuilder_ShouldHaveCorrectDefaults()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder().Build();

        // Assert
        props.TableName.Should().Be("test-table");
        props.PartitionKey.Should().BeNull();
        props.SortKey.Should().BeNull();
        props.RemovalPolicy.Should().Be(Amazon.CDK.RemovalPolicy.DESTROY);
        props.BillingMode.Should().Be(BillingMode.PAY_PER_REQUEST);
        props.GlobalSecondaryIndexes.Should().BeEmpty();
        props.Stream.Should().BeNull();
        props.TimeToLiveAttribute.Should().BeNull();
    }

    [Fact]
    public void WithTableName_ShouldSetTableName()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTableName("custom-table")
            .Build();

        // Assert
        props.TableName.Should().Be("custom-table");
    }

    [Fact]
    public void WithPartitionKey_ShouldSetPartitionKey_WithDefaultStringType()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithPartitionKey("userId")
            .Build();

        // Assert
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("userId");
        props.PartitionKey!.Type.Should().Be(AttributeType.STRING);
    }

    [Fact]
    public void WithPartitionKey_ShouldSetPartitionKey_WithSpecificType()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithPartitionKey("numericId", AttributeType.NUMBER)
            .Build();

        // Assert
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("numericId");
        props.PartitionKey!.Type.Should().Be(AttributeType.NUMBER);
    }

    [Fact]
    public void WithPartitionKey_ShouldSetPartitionKey_WithBinaryType()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithPartitionKey("binaryId", AttributeType.BINARY)
            .Build();

        // Assert
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("binaryId");
        props.PartitionKey!.Type.Should().Be(AttributeType.BINARY);
    }

    [Fact]
    public void WithSortKey_ShouldSetSortKey_WithDefaultStringType()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithSortKey("itemId")
            .Build();

        // Assert
        props.SortKey.Should().NotBeNull();
        props.SortKey!.Name.Should().Be("itemId");
        props.SortKey!.Type.Should().Be(AttributeType.STRING);
    }

    [Fact]
    public void WithSortKey_ShouldSetSortKey_WithSpecificType()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithSortKey("timestamp", AttributeType.NUMBER)
            .Build();

        // Assert
        props.SortKey.Should().NotBeNull();
        props.SortKey!.Name.Should().Be("timestamp");
        props.SortKey!.Type.Should().Be(AttributeType.NUMBER);
    }

    [Fact]
    public void WithRemovalPolicy_ShouldSetRemovalPolicy()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithRemovalPolicy(Amazon.CDK.RemovalPolicy.RETAIN)
            .Build();

        // Assert
        props.RemovalPolicy.Should().Be(Amazon.CDK.RemovalPolicy.RETAIN);
    }

    [Fact]
    public void WithBillingMode_ShouldSetBillingMode()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithBillingMode(BillingMode.PROVISIONED)
            .Build();

        // Assert
        props.BillingMode.Should().Be(BillingMode.PROVISIONED);
    }

    [Fact]
    public void WithGlobalSecondaryIndex_ShouldAddGsi_WithOnlyPartitionKey()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK", AttributeType.STRING)
            .Build();

        // Assert
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
        var gsi = props.GlobalSecondaryIndexes[0];
        gsi.IndexName.Should().Be("GSI1");
        gsi.PartitionKey.Should().NotBeNull();
        gsi.PartitionKey!.Name.Should().Be("gsiPK");
        gsi.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        gsi.SortKey.Should().BeNull();
    }

    [Fact]
    public void WithGlobalSecondaryIndex_ShouldAddGsi_WithPartitionAndSortKey()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK", AttributeType.STRING, "gsiSK", AttributeType.NUMBER)
            .Build();

        // Assert
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
        var gsi = props.GlobalSecondaryIndexes[0];
        gsi.IndexName.Should().Be("GSI1");
        gsi.PartitionKey.Should().NotBeNull();
        gsi.PartitionKey!.Name.Should().Be("gsiPK");
        gsi.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        gsi.SortKey.Should().NotBeNull();
        gsi.SortKey!.Name.Should().Be("gsiSK");
        gsi.SortKey!.Type.Should().Be(AttributeType.NUMBER);
    }

    [Fact]
    public void WithGlobalSecondaryIndex_ShouldAddMultipleGsis()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithGlobalSecondaryIndex("GSI1", "gsiPK1", AttributeType.STRING)
            .WithGlobalSecondaryIndex("GSI2", "gsiPK2", AttributeType.NUMBER, "gsiSK2", AttributeType.STRING)
            .Build();

        // Assert
        props.GlobalSecondaryIndexes.Should().HaveCount(2);
        
        var gsi1 = props.GlobalSecondaryIndexes[0];
        gsi1.IndexName.Should().Be("GSI1");
        gsi1.PartitionKey!.Name.Should().Be("gsiPK1");
        gsi1.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        gsi1.SortKey.Should().BeNull();
        
        var gsi2 = props.GlobalSecondaryIndexes[1];
        gsi2.IndexName.Should().Be("GSI2");
        gsi2.PartitionKey!.Name.Should().Be("gsiPK2");
        gsi2.PartitionKey!.Type.Should().Be(AttributeType.NUMBER);
        gsi2.SortKey!.Name.Should().Be("gsiSK2");
        gsi2.SortKey!.Type.Should().Be(AttributeType.STRING);
    }

    [Fact]
    public void WithStream_ShouldSetStream()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
            .Build();

        // Assert
        props.Stream.Should().Be(StreamViewType.NEW_AND_OLD_IMAGES);
    }

    [Fact]
    public void WithoutStream_ShouldSetStreamToNull()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
            .WithoutStream()
            .Build();

        // Assert
        props.Stream.Should().BeNull();
    }

    [Fact]
    public void WithTimeToLiveAttribute_ShouldSetTtlAttribute()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTimeToLiveAttribute("expiresAt")
            .Build();

        // Assert
        props.TimeToLiveAttribute.Should().Be("expiresAt");
    }

    [Fact]
    public void WithoutTimeToLiveAttribute_ShouldSetTtlAttributeToNull()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTimeToLiveAttribute("expiresAt")
            .WithoutTimeToLiveAttribute()
            .Build();

        // Assert
        props.TimeToLiveAttribute.Should().BeNull();
    }

    [Fact]
    public void ForUserTable_ShouldConfigureUserTableDefaults()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForUserTable()
            .Build();

        // Assert
        props.TableName.Should().Be("users");
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("userId");
        props.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        props.SortKey.Should().NotBeNull();
        props.SortKey!.Name.Should().Be("itemId");
        props.SortKey!.Type.Should().Be(AttributeType.STRING);
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
        
        var gsi = props.GlobalSecondaryIndexes[0];
        gsi.IndexName.Should().Be("GSI1");
        gsi.PartitionKey!.Name.Should().Be("itemType");
        gsi.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        gsi.SortKey!.Name.Should().Be("createdAt");
        gsi.SortKey!.Type.Should().Be(AttributeType.STRING);
    }

    [Fact]
    public void ForUserTable_ShouldConfigureUserTableWithCustomTableName()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForUserTable("custom-users")
            .Build();

        // Assert
        props.TableName.Should().Be("custom-users");
        props.PartitionKey!.Name.Should().Be("userId");
        props.SortKey!.Name.Should().Be("itemId");
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
    }

    [Fact]
    public void ForSessionTable_ShouldConfigureSessionTableDefaults()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForSessionTable()
            .Build();

        // Assert
        props.TableName.Should().Be("sessions");
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("sessionId");
        props.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        props.SortKey.Should().BeNull();
        props.TimeToLiveAttribute.Should().Be("expiresAt");
        props.Stream.Should().Be(StreamViewType.NEW_AND_OLD_IMAGES);
    }

    [Fact]
    public void ForSessionTable_ShouldConfigureSessionTableWithCustomTableName()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForSessionTable("custom-sessions")
            .Build();

        // Assert
        props.TableName.Should().Be("custom-sessions");
        props.PartitionKey!.Name.Should().Be("sessionId");
        props.TimeToLiveAttribute.Should().Be("expiresAt");
        props.Stream.Should().Be(StreamViewType.NEW_AND_OLD_IMAGES);
    }

    [Fact]
    public void ForEventSourcing_ShouldConfigureEventSourcingDefaults()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForEventSourcing()
            .Build();

        // Assert
        props.TableName.Should().Be("events");
        props.PartitionKey.Should().NotBeNull();
        props.PartitionKey!.Name.Should().Be("aggregateId");
        props.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        props.SortKey.Should().NotBeNull();
        props.SortKey!.Name.Should().Be("eventId");
        props.SortKey!.Type.Should().Be(AttributeType.STRING);
        props.Stream.Should().Be(StreamViewType.NEW_AND_OLD_IMAGES);
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
        
        var gsi = props.GlobalSecondaryIndexes[0];
        gsi.IndexName.Should().Be("GSI1");
        gsi.PartitionKey!.Name.Should().Be("eventType");
        gsi.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        gsi.SortKey!.Name.Should().Be("timestamp");
        gsi.SortKey!.Type.Should().Be(AttributeType.NUMBER);
    }

    [Fact]
    public void ForEventSourcing_ShouldConfigureEventSourcingWithCustomTableName()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .ForEventSourcing("custom-events")
            .Build();

        // Assert
        props.TableName.Should().Be("custom-events");
        props.PartitionKey!.Name.Should().Be("aggregateId");
        props.SortKey!.Name.Should().Be("eventId");
        props.Stream.Should().Be(StreamViewType.NEW_AND_OLD_IMAGES);
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
    }

    [Fact]
    public void Builder_ShouldSupportMethodChaining()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTableName("chained-table")
            .WithPartitionKey("pk", AttributeType.STRING)
            .WithSortKey("sk", AttributeType.NUMBER)
            .WithRemovalPolicy(Amazon.CDK.RemovalPolicy.RETAIN)
            .WithBillingMode(BillingMode.PROVISIONED)
            .WithGlobalSecondaryIndex("GSI1", "gsiPK")
            .WithStream(StreamViewType.NEW_IMAGE)
            .WithTimeToLiveAttribute("ttl")
            .Build();

        // Assert
        props.TableName.Should().Be("chained-table");
        props.PartitionKey!.Name.Should().Be("pk");
        props.PartitionKey!.Type.Should().Be(AttributeType.STRING);
        props.SortKey!.Name.Should().Be("sk");
        props.SortKey!.Type.Should().Be(AttributeType.NUMBER);
        props.RemovalPolicy.Should().Be(Amazon.CDK.RemovalPolicy.RETAIN);
        props.BillingMode.Should().Be(BillingMode.PROVISIONED);
        props.GlobalSecondaryIndexes.Should().HaveCount(1);
        props.Stream.Should().Be(StreamViewType.NEW_IMAGE);
        props.TimeToLiveAttribute.Should().Be("ttl");
    }

    [Fact]
    public void Builder_ShouldOverwritePreviousValues()
    {
        // Act
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTableName("first-table")
            .WithTableName("second-table")
            .WithPartitionKey("firstPK", AttributeType.STRING)
            .WithPartitionKey("secondPK", AttributeType.NUMBER)
            .WithStream(StreamViewType.NEW_IMAGE)
            .WithStream(StreamViewType.OLD_IMAGE)
            .WithTimeToLiveAttribute("firstTTL")
            .WithTimeToLiveAttribute("secondTTL")
            .Build();

        // Assert
        props.TableName.Should().Be("second-table");
        props.PartitionKey!.Name.Should().Be("secondPK");
        props.PartitionKey!.Type.Should().Be(AttributeType.NUMBER);
        props.Stream.Should().Be(StreamViewType.OLD_IMAGE);
        props.TimeToLiveAttribute.Should().Be("secondTTL");
    }

    [Fact]
    public void Builder_ShouldCreateValidTableWithAllFeatures()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var props = new DynamoDbTableConstructPropsBuilder()
            .WithTableName("full-featured-table")
            .WithPartitionKey("pk", AttributeType.STRING)
            .WithSortKey("sk", AttributeType.NUMBER)
            .WithGlobalSecondaryIndex("GSI1", "gsiPK1", AttributeType.STRING, "gsiSK1", AttributeType.NUMBER)
            .WithGlobalSecondaryIndex("GSI2", "gsiPK2", AttributeType.NUMBER)
            .WithStream(StreamViewType.NEW_AND_OLD_IMAGES)
            .WithTimeToLiveAttribute("expiresAt")
            .WithBillingMode(BillingMode.PAY_PER_REQUEST)
            .WithRemovalPolicy(Amazon.CDK.RemovalPolicy.DESTROY)
            .Build();

        // Act
        _ = new DynamoDbTableConstruct(stack, "TestTable", props);

        // Assert
        var template = Template.FromStack(stack);
        template.ShouldHaveDynamoTable("full-featured-table");
        template.ShouldHavePartitionKey("pk", AttributeType.STRING);
        template.ShouldHaveSortKey("sk", AttributeType.NUMBER);
        template.ShouldHaveGlobalSecondaryIndex("GSI1");
        template.ShouldHaveGlobalSecondaryIndex("GSI2");
        template.ShouldHaveGlobalSecondaryIndexCount(2);
        template.ShouldHaveTableStream(StreamViewType.NEW_AND_OLD_IMAGES);
        template.ShouldHaveTimeToLiveAttribute("expiresAt");
        template.ShouldHaveTableOutputs("test-stack", "TestTable");
    }

    #endregion
}
using Amazon.CDK;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Extensions;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class StackExtensionsTests
{
    [Fact]
    public void CreateExportName_ShouldReturnFormattedName_WhenWithinLengthLimit()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal("test-stack");
        var id = "MyConstruct";
        var qualifier = "Arn";

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Should().Be("test-stack-myconstruct-arn");
    }

    [Fact]
    public void CreateExportName_ShouldNormalizeToLowercase()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal("TEST-STACK");
        var id = "MyConstruct";
        var qualifier = "ARN";

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Should().Be("test-stack-myconstruct-arn");
    }

    [Fact]
    public void CreateExportName_ShouldTruncateAndAddHash_WhenExceedsLengthLimit()
    {
        // Arrange
        var longStackName = new string('a', 100);
        var longId = new string('b', 100);
        var longQualifier = new string('c', 100);
        var stack = CdkTestHelper.CreateTestStackMinimal(longStackName);

        // Act
        var result = stack.CreateExportName(longId, longQualifier);

        // Assert
        result.Length.Should().Be(256);
        result.Should().Contain("-");
        result.Should().Contain("-");
        
        // Should end with 8-character hash
        var parts = result.Split('-');
        parts.Last().Length.Should().Be(8);
        
        // Hash should be lowercase hexadecimal
        parts.Last().Should().MatchRegex("^[0-9a-f]{8}$");
    }

    [Fact]
    public void CreateExportName_ShouldReturnExactly256Characters_WhenTruncated()
    {
        // Arrange
        var longStackName = new string('a', 200);
        var longId = new string('b', 200);
        var longQualifier = new string('c', 200);
        var stack = CdkTestHelper.CreateTestStackMinimal(longStackName);

        // Act
        var result = stack.CreateExportName(longId, longQualifier);

        // Assert
        result.Length.Should().Be(256);
    }

    [Fact]
    public void CreateExportName_ShouldHandleEmptyQualifier()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal("test-stack");
        var id = "MyConstruct";
        var qualifier = "";

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Should().Be("test-stack-myconstruct-");
    }

    [Fact]
    public void CreateExportName_ShouldHandleEmptyId()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var id = "";
        var qualifier = "arn";

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Should().Be("test-stack--arn");
    }

    [Fact]
    public void CreateExportName_ShouldHandleSpecialCharacters()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var id = "My-Construct_123";
        var qualifier = "GSI.Index";

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Should().Be("test-stack-my-construct_123-gsi.index");
    }

    [Fact]
    public void CreateExportName_ShouldProduceDifferentHashes_ForDifferentInputs()
    {
        // Arrange
        var longStackName = new string('a', 200);
        var longId1 = new string('b', 200);
        var longId2 = new string('c', 200);
        var longQualifier = new string('d', 200);
        var stack = CdkTestHelper.CreateTestStackMinimal(longStackName);

        // Act
        var result1 = stack.CreateExportName(longId1, longQualifier);
        var result2 = stack.CreateExportName(longId2, longQualifier);

        // Assert
        result1.Should().NotBe(result2);
        result1.Length.Should().Be(256);
        result2.Length.Should().Be(256);
        
        // The hashes should be different
        var hash1 = result1.Split('-').Last();
        var hash2 = result2.Split('-').Last();
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void CreateExportName_ShouldProduceSameHash_ForSameInputs()
    {
        // Arrange
        var longStackName = new string('a', 200);
        var longId = new string('b', 200);
        var longQualifier = new string('c', 200);
        var stack = CdkTestHelper.CreateTestStackMinimal(longStackName);

        // Act
        var result1 = stack.CreateExportName(longId, longQualifier);
        var result2 = stack.CreateExportName(longId, longQualifier);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void CreateExportName_ShouldHandleExactly256CharacterInput()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var totalLength = "test-stack".Length + 2; // account for dashes
        var remainingLength = 256 - totalLength;
        var id = new string('a', remainingLength / 2);
        var qualifier = new string('b', remainingLength - (remainingLength / 2));

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Length.Should().Be(256);
        // Should not have hash suffix - check that it ends with the expected pattern
        result.Should().EndWith("-" + qualifier);
    }

    [Fact]
    public void CreateExportName_ShouldHandleExactly257CharacterInput()
    {
        // Arrange
        var stack = CdkTestHelper.CreateTestStackMinimal();
        var totalLength = "test-stack".Length + 2; // account for dashes
        var remainingLength = 257 - totalLength; // One character over limit
        var id = new string('a', remainingLength / 2);
        var qualifier = new string('b', remainingLength - (remainingLength / 2));

        // Act
        var result = stack.CreateExportName(id, qualifier);

        // Assert
        result.Length.Should().Be(256);
        result.Should().Contain("-");
        
        // Should have hash suffix
        var parts = result.Split('-');
        parts.Last().Length.Should().Be(8);
        parts.Last().Should().MatchRegex("^[0-9a-f]{8}$");
    }
}
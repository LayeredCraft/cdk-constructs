using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.DynamoDB;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Extension methods for asserting DynamoDbTableConstruct resources in CDK templates.
/// These helpers simplify common testing scenarios for consumers of the library.
/// </summary>
public static class DynamoDbTableConstructAssertions
{
    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified name.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="tableName">The expected table name</param>
    public static void ShouldHaveDynamoTable(this Template template, string tableName)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "TableName", tableName }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified partition key.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="keyName">The expected partition key name</param>
    /// <param name="keyType">The expected partition key type</param>
    public static void ShouldHavePartitionKey(this Template template, string keyName, AttributeType keyType)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "KeySchema", Match.ArrayWith(new[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    { "AttributeName", keyName },
                    { "KeyType", "HASH" }
                })
            })},
            { "AttributeDefinitions", Match.ArrayWith(new[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    { "AttributeName", keyName },
                    { "AttributeType", keyType == AttributeType.STRING ? "S" : 
                                      keyType == AttributeType.NUMBER ? "N" : "B" }
                })
            })}
        }));
    }

    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified sort key.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="keyName">The expected sort key name</param>
    /// <param name="keyType">The expected sort key type</param>
    public static void ShouldHaveSortKey(this Template template, string keyName, AttributeType keyType)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "KeySchema", Match.ArrayWith(new[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    { "AttributeName", keyName },
                    { "KeyType", "RANGE" }
                })
            })},
            { "AttributeDefinitions", Match.ArrayWith(new[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    { "AttributeName", keyName },
                    { "AttributeType", keyType == AttributeType.STRING ? "S" : 
                                      keyType == AttributeType.NUMBER ? "N" : "B" }
                })
            })}
        }));
    }

    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified global secondary index.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="indexName">The expected GSI name</param>
    public static void ShouldHaveGlobalSecondaryIndex(this Template template, string indexName)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "GlobalSecondaryIndexes", Match.ArrayWith(new[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    { "IndexName", indexName }
                })
            })}
        }));
    }

    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified stream configuration.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="streamType">The expected stream view type</param>
    public static void ShouldHaveTableStream(this Template template, StreamViewType streamType)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "StreamSpecification", Match.ObjectLike(new Dictionary<string, object>
            {
                { "StreamViewType", streamType.ToString() }
            })}
        }));
    }

    /// <summary>
    /// Asserts that the template contains a DynamoDB table with the specified TTL attribute.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="attributeName">The expected TTL attribute name</param>
    public static void ShouldHaveTimeToLiveAttribute(this Template template, string attributeName)
    {
        template.HasResourceProperties("AWS::DynamoDB::Table", Match.ObjectLike(new Dictionary<string, object>
        {
            { "TimeToLiveSpecification", Match.ObjectLike(new Dictionary<string, object>
            {
                { "AttributeName", attributeName },
                { "Enabled", true }
            })}
        }));
    }

    /// <summary>
    /// Asserts that the template contains the standard CloudFormation outputs for a DynamoDB table.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="stackName">The stack name used in output export names</param>
    /// <param name="constructId">The construct ID used in output export names</param>
    public static void ShouldHaveTableOutputs(this Template template, string stackName, string constructId)
    {
        var normalizedStackName = stackName.ToLowerInvariant();
        var normalizedConstructId = constructId.ToLowerInvariant();
        
        // Check that outputs exist by looking for export names (CloudFormation generates logical IDs with hashes)
        var templateJson = template.ToJSON();
        if (!templateJson.TryGetValue("Outputs", out var value1))
        {
            throw new InvalidOperationException("Template does not contain any outputs");
        }
        
        var outputs = (IDictionary<string, object>)value1;
        
        // Verify export names contain the expected values
        bool hasArnOutput = false, hasNameOutput = false;
        
        foreach (var output in outputs.Values)
        {
            var outputDict = (IDictionary<string, object>)output;
            if (outputDict.TryGetValue("Export", out var value))
            {
                var export = (IDictionary<string, object>)value;
                var exportName = export["Name"].ToString();
                if (exportName == $"{normalizedStackName}-{normalizedConstructId}-arn") hasArnOutput = true;
                else if (exportName == $"{normalizedStackName}-{normalizedConstructId}-name") hasNameOutput = true;
            }
        }
        
        if (!hasArnOutput)
        {
            throw new InvalidOperationException($"ARN output with export name '{normalizedStackName}-{normalizedConstructId}-arn' should exist");
        }
        
        if (!hasNameOutput)
        {
            throw new InvalidOperationException($"Name output with export name '{normalizedStackName}-{normalizedConstructId}-name' should exist");
        }
    }

    /// <summary>
    /// Asserts that the template contains the expected number of global secondary indexes.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="count">The expected number of GSIs</param>
    public static void ShouldHaveGlobalSecondaryIndexCount(this Template template, int count)
    {
        var resources = template.FindResources("AWS::DynamoDB::Table");
        
        foreach (var resource in resources.Values)
        {
            if (resource is Dictionary<string, object> resourceDict &&
                resourceDict.TryGetValue("Properties", out var properties) &&
                properties is Dictionary<string, object> propsDict)
            {
                if (propsDict.TryGetValue("GlobalSecondaryIndexes", out var gsiValue) &&
                    gsiValue is IList<object> gsiList)
                {
                    if (gsiList.Count != count)
                        throw new InvalidOperationException($"Expected {count} Global Secondary Indexes but found {gsiList.Count}");
                    return;
                }
            }
        }
        
        if (count == 0)
        {
            // If expecting 0 GSIs, we should not find any GlobalSecondaryIndexes property
            return;
        }
        
        throw new InvalidOperationException($"Expected {count} Global Secondary Indexes but found none");
    }
}
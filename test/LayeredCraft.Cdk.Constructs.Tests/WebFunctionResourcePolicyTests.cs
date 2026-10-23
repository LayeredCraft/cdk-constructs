using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionResourcePolicyTests
{
    private const string DistributionA = "arn:aws:cloudfront::123456789012:distribution/E1111111111111";
    private const string DistributionB = "arn:aws:cloudfront::123456789012:distribution/E2222222222222";

    private static JsonElement TemplateJson(Stack stack)
    {
        var template = Template.FromStack(stack);
        var json = JsonSerializer.Serialize(template.ToJSON());
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Resources(JsonElement template) => template.GetProperty("Resources");

    private static JsonElement? ResourceOfType(JsonElement template, string type)
    {
        foreach (var property in Resources(template).EnumerateObject())
        {
            if (property.Value.GetProperty("Type").GetString() == type)
                return property.Value;
        }

        return null;
    }

    private static List<JsonElement> ResourcesOfType(JsonElement template, string type)
    {
        var result = new List<JsonElement>();
        foreach (var property in Resources(template).EnumerateObject())
        {
            if (property.Value.GetProperty("Type").GetString() == type)
                result.Add(property.Value);
        }

        return result;
    }

    private static string LogicalIdOfType(JsonElement template, string type) =>
        Resources(template).EnumerateObject()
            .First(property => property.Value.GetProperty("Type").GetString() == type)
            .Name;

    private static JsonElement ResourcesOfTypeStatements(JsonElement template, string type)
    {
        var policy = ResourceOfType(template, type)!.Value;
        return policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");
    }

    private static WebFunctionConstruct CreateFunction(
        Stack stack,
        out WebFunctionEndpointConstruct defaultEndpoint,
        string functionId = "web-function")
    {
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var construct = new WebFunctionConstruct(stack, functionId, new WebFunctionConstructProps
        {
            FunctionName = "test-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });
        defaultEndpoint = construct.DefaultEndpoint;
        return construct;
    }

    [Fact]
    public void Construct_ShouldNotCreateResourcePolicyWhenNoStatements()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        ResourceOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeNull();
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldCreateSinglePolicyScopedToEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantCloudFrontInvoke(DistributionA);

        var template = TemplateJson(stack);
        var policy = ResourceOfType(template, "AWS::Lambda::ResourcePolicy")!.Value;
        var functionId = LogicalIdOfType(template, "AWS::Lambda::WebFunction");
        policy.GetProperty("Properties").GetProperty("ResourceArn").GetProperty("Fn::GetAtt")[0].GetString()
            .Should().Be(functionId);

        var statements = ResourcesOfTypeStatements(template, "AWS::Lambda::ResourcePolicy");
        statements.GetArrayLength().Should().Be(1);

        var statement = statements[0];
        statement.GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
        statement.GetProperty("Effect").GetString().Should().Be("Allow");
        statement.GetProperty("Principal").GetProperty("Service").GetString()
            .Should().Be("cloudfront.amazonaws.com");
        statement.GetProperty("Condition").GetProperty("StringEquals").GetProperty("aws:SourceArn").GetString()
            .Should().Be(DistributionA);

        var endpointArn = statement.GetProperty("Resource");
        var endpointId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionEndpoint");
        endpointArn.GetProperty("Fn::GetAtt")[0].GetString().Should().Be(endpointId);
        endpointArn.GetProperty("Fn::GetAtt")[1].GetString().Should().Be("EndpointArn");
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldAddStatementPerDistribution()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantCloudFrontInvoke(DistributionA);
        endpoint.GrantCloudFrontInvoke(DistributionB);

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
        ResourcesOfTypeStatements(template, "AWS::Lambda::ResourcePolicy").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldDeduplicateRepeatedGrant()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantCloudFrontInvoke(DistributionA);
        endpoint.GrantCloudFrontInvoke(DistributionA);

        var template = TemplateJson(stack);
        ResourcesOfTypeStatements(template, "AWS::Lambda::ResourcePolicy").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldProduceEqualStatementsRegardlessOfOrder()
    {
        var (_, stack1) = CdkTestHelper.CreateTestStack("order-a");
        _ = CreateFunction(stack1, out var endpoint1);
        endpoint1.GrantCloudFrontInvoke(DistributionA);
        endpoint1.GrantCloudFrontInvoke(DistributionB);

        var (_, stack2) = CdkTestHelper.CreateTestStack("order-b");
        _ = CreateFunction(stack2, out var endpoint2);
        endpoint2.GrantCloudFrontInvoke(DistributionB);
        endpoint2.GrantCloudFrontInvoke(DistributionA);

        static string StatementSet(Stack stack)
        {
            var statements = ResourcesOfTypeStatements(TemplateJson(stack), "AWS::Lambda::ResourcePolicy");
            var serialized = statements.EnumerateArray().Select(s => JsonSerializer.Serialize(s)).ToList();
            serialized.Sort(StringComparer.Ordinal);
            return string.Join("|", serialized);
        }

        StatementSet(stack1).Should().Be(StatementSet(stack2));
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldScopeStatementsPerEndpointInOnePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out var defaultEndpoint);
        var second = construct.AddEndpoint("public", new WebFunctionEndpointOptions
        {
            EndpointName = "public",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        defaultEndpoint.GrantCloudFrontInvoke(DistributionA);
        second.GrantCloudFrontInvoke(DistributionB);

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);

        var statements = ResourcesOfTypeStatements(template, "AWS::Lambda::ResourcePolicy");
        statements.GetArrayLength().Should().Be(2);

        var targets = statements.EnumerateArray()
            .Select(statement => statement.GetProperty("Resource").GetProperty("Fn::GetAtt")[1].GetString())
            .ToList();
        targets.Should().Contain("EndpointArn");
        var endpointArns = statements.EnumerateArray()
            .Select(statement => statement.GetProperty("Resource").GetProperty("Fn::GetAtt")[0].GetString())
            .ToList();
        endpointArns.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldThrowForApplicationManagedEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var managed = construct.AddEndpoint("managed", new WebFunctionEndpointOptions
        {
            EndpointName = "managed",
            AuthType = WebFunctionAuthType.ApplicationManaged,
        });

        var act = () => managed.GrantCloudFrontInvoke(DistributionA);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("not-an-arn")]
    [InlineData("arn:aws:lambda:us-east-1:123456789012:function:foo")]
    [InlineData("arn:aws:cloudfront::123456789012:distribution")]
    public void GrantCloudFrontInvoke_ShouldThrowForInvalidLiteralArn(string distributionArn)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantCloudFrontInvoke(distributionArn);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GrantCloudFrontInvoke_ShouldAcceptTokenArn()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantCloudFrontInvoke(Fn.Ref("DistributionArn"));

        act.Should().NotThrow();
        ResourcesOfTypeStatements(TemplateJson(stack), "AWS::Lambda::ResourcePolicy")
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AddToResourcePolicy_ShouldThrowForForeignResource()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = ["arn:aws:lambda:us-east-1:123456789012:web-function/other/endpoint/other"],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddToResourcePolicy_ShouldThrowForWildcardResource()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = ["*"],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddToResourcePolicy_ShouldAcceptEndpointArn()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().NotThrow();
        ResourcesOfTypeStatements(TemplateJson(stack), "AWS::Lambda::ResourcePolicy")
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AddEndpoint_ShouldRejectDuplicateDefaultName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);

        var act = () => construct.AddEndpoint("second-default", new WebFunctionEndpointOptions
        {
            EndpointName = "default",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEndpoint_ShouldRejectDuplicateCustomName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        construct.AddEndpoint("public", new WebFunctionEndpointOptions
        {
            EndpointName = "public",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var act = () => construct.AddEndpoint("public-2", new WebFunctionEndpointOptions
        {
            EndpointName = "public",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEndpoint_ShouldTrackAllEndpointsAndRouteEachToCurrentRevision()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out var defaultEndpoint);

        var managed = construct.AddEndpoint("managed", new WebFunctionEndpointOptions
        {
            EndpointName = "managed",
            AuthType = WebFunctionAuthType.ApplicationManaged,
        });
        var regional = construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.MultiRegion,
            Regions = ["us-west-2"],
        });

        construct.Endpoints.Should().BeEquivalentTo([defaultEndpoint, managed, regional], options => options.WithStrictOrdering());

        var template = TemplateJson(stack);
        var endpoints = ResourcesOfType(template, "AWS::Lambda::WebFunctionEndpoint");
        endpoints.Should().HaveCount(3);

        foreach (var endpointResource in endpoints)
        {
            var weights = endpointResource.GetProperty("Properties").GetProperty("RevisionWeights");
            weights.GetArrayLength().Should().Be(1);
            weights[0].GetProperty("Weight").GetDouble().Should().Be(100);
        }
    }

    [Fact]
    public void WebFunctionConstruct_ShouldNotExposePublicAddToResourcePolicy()
    {
        typeof(WebFunctionConstruct)
            .GetMethods()
            .Where(method => method.IsPublic)
            .Select(method => method.Name)
            .Should().NotContain("AddToResourcePolicy");
    }

    [Fact]
    public void WebFunctionEndpointConstruct_ShouldImplementResourceWithPolicy()
    {
        typeof(IResourceWithPolicyV2).IsAssignableFrom(typeof(WebFunctionEndpointConstruct)).Should().BeTrue();
    }
}

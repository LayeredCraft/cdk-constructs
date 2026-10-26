using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using Constructs;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Synthesis tests for <see cref="WebFunctionOrigin"/> (design §8.5, §11.1, §15).
/// </summary>
[Collection("CDK Tests")]
public class WebFunctionOriginTests
{
    private static JsonElement TemplateJson(Stack stack)
    {
        var template = Template.FromStack(stack);
        var json = JsonSerializer.Serialize(template.ToJSON());
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Resources(JsonElement template) => template.GetProperty("Resources");

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

    private static JsonElement Statements(JsonElement template)
    {
        var policy = ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Single();
        return policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");
    }

    private static string LogicalIdOfType(JsonElement template, string type) =>
        Resources(template).EnumerateObject()
            .First(property => property.Value.GetProperty("Type").GetString() == type)
            .Name;

    private static JsonElement DistributionOrigin(JsonElement template)
    {
        var distribution = ResourcesOfType(template, "AWS::CloudFront::Distribution").Single();
        return distribution.GetProperty("Properties").GetProperty("DistributionConfig")
            .GetProperty("Origins")[0];
    }

    private static string DistributionIdFromArn(JsonElement sourceArn)
    {
        var parts = sourceArn.GetProperty("Fn::Join")[1];
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("Ref", out var reference))
            {
                var value = reference.GetString();
                if (value is not null && value != "AWS::Partition")
                    return value;
            }
        }

        throw new InvalidOperationException("The aws:SourceArn did not reference a distribution.");
    }

    private static List<string> InfoAnnotations(IConstruct root)
    {
        var result = new List<string>();
        foreach (var construct in root.Node.FindAll())
        {
            foreach (var entry in construct.Node.Metadata)
            {
                if (entry.Type == "aws:cdk:info" && entry.Data is string message)
                    result.Add(message);
            }
        }

        return result;
    }

    private static string ExternallyManagedAnnotation(IConstruct root) =>
        InfoAnnotations(root)
            .Should().ContainSingle(message => message.Contains("externally managed", StringComparison.Ordinal))
            .Which;

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

    private static Distribution CreateDistribution(Stack stack, string id, IOrigin origin) =>
        new(stack, id, new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions { Origin = origin },
        });

    private static WebFunctionOriginWithOacProps FullOriginProps() => new()
    {
        OriginId = "web-origin",
        OriginPath = "/app",
        CustomHeaders = new Dictionary<string, string> { ["x-origin-verify"] = "secret" },
        ConnectionAttempts = 2,
        ConnectionTimeout = Duration.Seconds(10),
        ReadTimeout = Duration.Seconds(45),
        KeepaliveTimeout = Duration.Seconds(10),
        ResponseCompletionTimeout = Duration.Seconds(50),
        OriginShieldEnabled = true,
        OriginShieldRegion = "us-east-1",
    };

    private static void AssertOriginProps(JsonElement origin)
    {
        origin.GetProperty("Id").GetString().Should().Be("web-origin");
        origin.GetProperty("OriginPath").GetString().Should().Be("/app");
        origin.GetProperty("ConnectionAttempts").GetInt32().Should().Be(2);
        origin.GetProperty("ConnectionTimeout").GetInt32().Should().Be(10);
        origin.GetProperty("ResponseCompletionTimeout").GetInt32().Should().Be(50);

        var customOrigin = origin.GetProperty("CustomOriginConfig");
        customOrigin.GetProperty("OriginProtocolPolicy").GetString().Should().Be("https-only");
        customOrigin.GetProperty("OriginReadTimeout").GetInt32().Should().Be(45);
        customOrigin.GetProperty("OriginKeepaliveTimeout").GetInt32().Should().Be(10);

        var headers = origin.GetProperty("OriginCustomHeaders");
        headers.GetArrayLength().Should().Be(1);
        headers[0].GetProperty("HeaderName").GetString().Should().Be("x-origin-verify");
        headers[0].GetProperty("HeaderValue").GetString().Should().Be("secret");

        var shield = origin.GetProperty("OriginShield");
        shield.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        shield.GetProperty("OriginShieldRegion").GetString().Should().Be("us-east-1");
    }

    [Fact]
    public void WithOriginAccessControl_ShouldCreateLambdaOacWithSigV4_ForHomeRegion()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var template = TemplateJson(stack);
        var oac = ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Single();
        var properties = oac.GetProperty("Properties").GetProperty("OriginAccessControlConfig");
        properties.GetProperty("OriginAccessControlOriginType").GetString().Should().Be("lambda");
        properties.GetProperty("SigningBehavior").GetString().Should().Be("always-amz-auth");
        properties.GetProperty("SigningProtocol").GetString().Should().Be("sigv4");
    }

    [Fact]
    public void WithOriginAccessControl_ShouldCreateLambdaOacWithSigV4_ForPerRegion()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var endpoint = construct.AddEndpoint("per-region", new WebFunctionEndpointOptions
        {
            EndpointName = "per-region",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var oac = ResourcesOfType(TemplateJson(stack), "AWS::CloudFront::OriginAccessControl").Single();
        oac.GetProperty("Properties").GetProperty("OriginAccessControlConfig").GetProperty("SigningProtocol").GetString().Should().Be("sigv4");
    }

    [Fact]
    public void WithOriginAccessControl_ShouldCreateLambdaOacWithSigV4A_ForMultiRegion()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var endpoint = construct.AddEndpoint("multi-region", new WebFunctionEndpointOptions
        {
            EndpointName = "multi-region",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.MultiRegion,
            Regions = ["us-west-2"],
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var oac = ResourcesOfType(TemplateJson(stack), "AWS::CloudFront::OriginAccessControl").Single();
        oac.GetProperty("Properties").GetProperty("OriginAccessControlConfig").GetProperty("SigningProtocol").GetString().Should().Be("sigv4a");
    }

    [Fact]
    public void WithOriginAccessControl_ShouldConfigureHttpsOriginFromEndpointDomain()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var template = TemplateJson(stack);
        var origin = DistributionOrigin(template);
        var endpointId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionEndpoint");
        origin.GetProperty("DomainName").GetProperty("Fn::GetAtt")[0].GetString().Should().Be(endpointId);
        origin.GetProperty("DomainName").GetProperty("Fn::GetAtt")[1].GetString().Should().Be("DomainName");
        origin.GetProperty("CustomOriginConfig").GetProperty("OriginProtocolPolicy").GetString().Should().Be("https-only");

        var oacId = LogicalIdOfType(template, "AWS::CloudFront::OriginAccessControl");
        origin.GetProperty("OriginAccessControlId").GetProperty("Fn::GetAtt")[0].GetString().Should().Be(oacId);
        origin.GetProperty("OriginAccessControlId").GetProperty("Fn::GetAtt")[1].GetString().Should().Be("Id");
    }

    [Fact]
    public void WithOriginAccessControl_ShouldForwardAllOriginProps()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var origin = WebFunctionOrigin.WithOriginAccessControl(endpoint, FullOriginProps());

        CreateDistribution(stack, "distribution", origin);

        AssertOriginProps(DistributionOrigin(TemplateJson(stack)));
    }

    [Fact]
    public void WithOriginAccessControl_InvalidConnectionAttempts_ShouldFailBeforeCreatingResources()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => WebFunctionOrigin.WithOriginAccessControl(endpoint, new WebFunctionOriginWithOacProps
        {
            ConnectionAttempts = 5,
        });

        // The rejection is produced by CDK's own HttpOrigin validation, so its exception type and message
        // are owned by jsii and can vary under parallel test execution. Assert only that the invalid
        // configuration is rejected before any resource is created.
        act.Should().Throw<Exception>();

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void WithOriginAccessControl_ShouldGrantDistributionViaSourceArn()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);

        var statements = Statements(template);
        statements.GetArrayLength().Should().Be(1);
        var statement = statements[0];
        statement.GetProperty("Effect").GetString().Should().Be("Allow");
        statement.GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
        statement.GetProperty("Principal").GetProperty("Service").GetString().Should().Be("cloudfront.amazonaws.com");

        var sourceArn = statement.GetProperty("Condition").GetProperty("StringEquals").GetProperty("aws:SourceArn");
        DistributionIdFromArn(sourceArn).Should().Be(LogicalIdOfType(template, "AWS::CloudFront::Distribution"));
    }

    [Fact]
    public void WithOriginAccessControl_SameOriginOnTwoBehaviors_ShouldBindOnce()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var origin = WebFunctionOrigin.WithOriginAccessControl(endpoint);
        _ = new Distribution(stack, "distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions { Origin = origin },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/_next/static/*"] = new BehaviorOptions { Origin = origin },
            },
        });

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().HaveCount(1);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
        Statements(template).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void WithOriginAccessControl_TwoDistributions_ShouldScopeEachStatementAndKeepOnePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        CreateDistribution(stack, "distribution-a", WebFunctionOrigin.WithOriginAccessControl(endpoint));
        CreateDistribution(stack, "distribution-b", WebFunctionOrigin.WithOriginAccessControl(endpoint));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().HaveCount(2);

        var statements = Statements(template);
        statements.GetArrayLength().Should().Be(2);

        var distributionIds = Resources(template).EnumerateObject()
            .Where(p => p.Value.GetProperty("Type").GetString() == "AWS::CloudFront::Distribution")
            .Select(p => p.Name)
            .ToList();

        var referenced = statements.EnumerateArray()
            .Select(s => DistributionIdFromArn(s.GetProperty("Condition").GetProperty("StringEquals")
                .GetProperty("aws:SourceArn")))
            .ToList();

        referenced.Should().BeEquivalentTo(distributionIds);
        referenced.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void WithOriginAccessControl_SameOriginInstanceAcrossDistributions_ShouldScopeEachGrant()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var origin = WebFunctionOrigin.WithOriginAccessControl(endpoint);
        CreateDistribution(stack, "distribution-a", origin);
        CreateDistribution(stack, "distribution-b", origin);

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().HaveCount(2);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);

        var statements = Statements(template);
        statements.GetArrayLength().Should().Be(2);

        var distributionIds = Resources(template).EnumerateObject()
            .Where(p => p.Value.GetProperty("Type").GetString() == "AWS::CloudFront::Distribution")
            .Select(p => p.Name)
            .ToList();

        var referenced = statements.EnumerateArray()
            .Select(s => DistributionIdFromArn(s.GetProperty("Condition").GetProperty("StringEquals")
                .GetProperty("aws:SourceArn")))
            .ToList();

        referenced.Should().BeEquivalentTo(distributionIds);
        referenced.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void WithOriginAccessControl_SecondEndpointOrigin_ShouldScopeStatementToThatEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var second = construct.AddEndpoint("public", new WebFunctionEndpointOptions
        {
            EndpointName = "public",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(second));

        var template = TemplateJson(stack);
        var statements = Statements(template);
        statements.GetArrayLength().Should().Be(1);

        var endpointIds = Resources(template).EnumerateObject()
            .Where(p => p.Value.GetProperty("Type").GetString() == "AWS::Lambda::WebFunctionEndpoint")
            .Select(p => p.Name)
            .ToList();

        var target = statements[0].GetProperty("Resource").GetProperty("Fn::GetAtt")[0].GetString();
        target.Should().Be(endpointIds.Single(id => id.Contains("public", StringComparison.Ordinal)));
    }

    [Fact]
    public void WithOriginAccessControl_SuppliedOac_ShouldBeReusedAndNotCreateAnother()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var supplied = new FunctionUrlOriginAccessControl(stack, "SharedOac", new FunctionUrlOriginAccessControlProps
        {
            Signing = Signing.SIGV4_ALWAYS_AMZ_AUTH,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint,
            new WebFunctionOriginWithOacProps { OriginAccessControl = supplied }));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().HaveCount(1);
        var origin = DistributionOrigin(template);
        origin.GetProperty("OriginAccessControlId").GetProperty("Fn::GetAtt")[0].GetString()
            .Should().Be(LogicalIdOfType(template, "AWS::CloudFront::OriginAccessControl"));
    }

    [Fact]
    public void WithOriginAccessControl_ExternallyManagedOwnedEndpoint_ShouldAnnotateWithGrantGuidance()
    {
        var (app, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(endpoint,
            new WebFunctionOriginWithOacProps { Permissions = WebFunctionOriginPermissions.ExternallyManaged }));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();

        var message = ExternallyManagedAnnotation(app);
        message.Should().Contain("test-stack/web-function/DefaultEndpoint");
        message.Should().Contain("test-stack/distribution");
        message.Should().Contain("GrantCloudFrontInvoke");
        message.Should().Contain("cloudfront.amazonaws.com");
        message.Should().Contain("aws:SourceArn");
    }

    [Fact]
    public void WithOriginAccessControl_ExternallyManagedImportedEndpoint_ShouldAnnotateWithOwnerGuidance()
    {
        var (app, stack) = CdkTestHelper.CreateTestStack();
        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(imported,
            new WebFunctionOriginWithOacProps { Permissions = WebFunctionOriginPermissions.ExternallyManaged }));

        var message = ExternallyManagedAnnotation(app);
        message.Should().Contain("imported endpoint");
        message.Should().Contain("arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default");
        message.Should().Contain("test-stack/distribution");
        message.Should().Contain("cloudfront.amazonaws.com");
        message.Should().Contain("aws:SourceArn");
        message.Should().Contain("owner");
        message.Should().NotContain("GrantCloudFrontInvoke");
    }

    [Fact]
    public void WithOriginAccessControl_ExternallyManagedTokenEndpoint_ShouldIdentifyEndpointWithoutTokenPlaceholders()
    {
        var (app, stack) = CdkTestHelper.CreateTestStack();
        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.ImportValue("SharedEndpointArn"),
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(imported,
            new WebFunctionOriginWithOacProps { Permissions = WebFunctionOriginPermissions.ExternallyManaged }));

        var message = ExternallyManagedAnnotation(app);
        message.Should().Contain("abc123.lambda-web.us-east-1.on.aws");
        message.Should().Contain("test-stack/distribution");
        message.Should().Contain("cloudfront.amazonaws.com");
        message.Should().NotContain("Token");
        message.Should().NotContain("${");
    }

    [Fact]
    public void WithOriginAccessControl_ImportedEndpoint_Automatic_ShouldThrow()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var act = () => CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(imported));

        act.Should().Throw<Exception>()
            .WithMessage("*imported Web Function endpoint*ExternallyManaged*");
    }

    [Fact]
    public void WithOriginAccessControl_ImportedEndpoint_ExternallyManaged_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(imported,
            new WebFunctionOriginWithOacProps { Permissions = WebFunctionOriginPermissions.ExternallyManaged }));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
        DistributionOrigin(template).GetProperty("DomainName").GetString()
            .Should().Be("abc123.lambda-web.us-east-1.on.aws");
    }

    [Fact]
    public void WithOriginAccessControl_TokenArnImportedEndpoint_ExternallyManaged_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.ImportValue("SharedEndpointArn"),
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithOriginAccessControl(imported,
            new WebFunctionOriginWithOacProps { Permissions = WebFunctionOriginPermissions.ExternallyManaged }));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
        DistributionOrigin(template).GetProperty("DomainName").GetString()
            .Should().Be("abc123.lambda-web.us-east-1.on.aws");
    }

    [Fact]
    public void WithOriginAccessControl_CrossStack_Automatic_ShouldThrowLibraryError()
    {
        var app = new App();
        var env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" };
        var functionStack = new Stack(app, "function-stack", new StackProps { Env = env });
        var distributionStack = new Stack(app, "distribution-stack", new StackProps { Env = env });

        _ = CreateFunction(functionStack, out var endpoint);

        var act = () => CreateDistribution(distributionStack, "distribution",
            WebFunctionOrigin.WithOriginAccessControl(endpoint));

        act.Should().Throw<Exception>()
            .WithMessage("*same stack*");
    }

    [Fact]
    public void WithOriginAccessControl_ApplicationManaged_ShouldThrowAtFactory()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var managed = construct.AddEndpoint("managed", new WebFunctionEndpointOptions
        {
            EndpointName = "managed",
            AuthType = WebFunctionAuthType.ApplicationManaged,
        });

        var act = () => WebFunctionOrigin.WithOriginAccessControl(managed);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithOriginAccessControl_NullEndpoint_ShouldThrow()
    {
        var act = () => WebFunctionOrigin.WithOriginAccessControl(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithoutOriginAccessControl_ApplicationManaged_ShouldCreateHttpsOriginWithoutOac()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var managed = construct.AddEndpoint("managed", new WebFunctionEndpointOptions
        {
            EndpointName = "managed",
            AuthType = WebFunctionAuthType.ApplicationManaged,
        });

        CreateDistribution(stack, "distribution", WebFunctionOrigin.WithoutOriginAccessControl(managed));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::CloudFront::OriginAccessControl").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();

        var origin = DistributionOrigin(template);
        origin.GetProperty("CustomOriginConfig").GetProperty("OriginProtocolPolicy").GetString().Should().Be("https-only");
        origin.TryGetProperty("OriginAccessControlId", out _).Should().BeFalse();
    }

    [Fact]
    public void WithoutOriginAccessControl_ShouldForwardAllOriginProps()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        var managed = construct.AddEndpoint("managed", new WebFunctionEndpointOptions
        {
            EndpointName = "managed",
            AuthType = WebFunctionAuthType.ApplicationManaged,
        });

        var origin = WebFunctionOrigin.WithoutOriginAccessControl(managed, FullOriginProps());

        CreateDistribution(stack, "distribution", origin);

        var originJson = DistributionOrigin(TemplateJson(stack));
        AssertOriginProps(originJson);
        originJson.TryGetProperty("OriginAccessControlId", out _).Should().BeFalse();
    }

    [Fact]
    public void WithoutOriginAccessControl_IamAuth_ShouldThrowAtFactory()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => WebFunctionOrigin.WithoutOriginAccessControl(endpoint);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithoutOriginAccessControl_NullEndpoint_ShouldThrow()
    {
        var act = () => WebFunctionOrigin.WithoutOriginAccessControl(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using Constructs;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionInvokeGrantTests
{
    private const string SameAccountRoleArn = "arn:aws:iam::123456789012:role/caller";
    private const string OtherAccountRoleArn = "arn:aws:iam::222233334444:role/partner";
    private const string EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default";

    private static JsonElement TemplateJson(Stack stack)
    {
        var template = Template.FromStack(stack);
        var json = JsonSerializer.Serialize(template.ToJSON());
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string Serialize(Stack stack) => JsonSerializer.Serialize(TemplateJson(stack));

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

    private static JsonElement ResourceByLogicalIdPrefix(JsonElement template, string prefix)
    {
        foreach (var property in Resources(template).EnumerateObject())
        {
            if (property.Name.StartsWith(prefix, StringComparison.Ordinal))
                return property.Value;
        }

        throw new InvalidOperationException($"No resource with logical id prefix '{prefix}'.");
    }

    private static string LogicalIdOfType(JsonElement template, string type) =>
        Resources(template).EnumerateObject()
            .First(property => property.Value.GetProperty("Type").GetString() == type)
            .Name;

    private static JsonElement ResourcePolicyStatements(JsonElement template) =>
        ResourceOfType(template, "AWS::Lambda::ResourcePolicy")!
            .Value.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");

    private sealed class FakeResourceWithPolicy : Amazon.CDK.Resource, IResourceWithPolicyV2
    {
        private readonly bool _statementAdded;

        public FakeResourceWithPolicy(Construct scope, string id, bool statementAdded)
            : base(scope, id) => _statementAdded = statementAdded;

        public AddToResourcePolicyResult AddToResourcePolicy(PolicyStatement statement)
            => new() { StatementAdded = _statementAdded };

        IAddToResourcePolicyResult IResourceWithPolicyV2.AddToResourcePolicy(PolicyStatement statement)
            => AddToResourcePolicy(statement);
    }

    private static WebFunctionConstruct CreateFunction(
        Stack stack,
        out WebFunctionEndpointConstruct defaultEndpoint,
        WebFunctionAuthType authType = WebFunctionAuthType.IamAuth,
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
            Endpoint = new WebFunctionEndpointOptions { AuthType = authType },
        });
        defaultEndpoint = construct.DefaultEndpoint;
        return construct;
    }

    private static Role OwnedRole(Stack stack, string id = "CallerRole") =>
        new(stack, id, new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });

    [Fact]
    public void GrantInvoke_OwnedRoleAutomatic_WritesIdentityPolicyOnEndpointArnOnly()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var role = OwnedRole(stack);

        endpoint.GrantInvoke(role);

        var template = TemplateJson(stack);
        ResourceOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeNull();

        var endpointId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionEndpoint");
        var policies = ResourcesOfType(template, "AWS::IAM::Policy");
        policies.Should().HaveCount(1);
        var statements = policies[0].GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");
        statements.GetArrayLength().Should().Be(1);
        statements[0].GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
        statements[0].GetProperty("Resource").GetProperty("Fn::GetAtt")[0].GetString().Should().Be(endpointId);
    }

    [Fact]
    public void GrantInvoke_OwnedWebFunctionGrantee_WritesIdentityPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var artifacts = Bucket.FromBucketName(stack, "CallerArtifacts", "caller-artifacts");
        var caller = new WebFunctionConstruct(stack, "caller", new WebFunctionConstructProps
        {
            FunctionName = "caller-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(caller);

        act.Should().NotThrow();
        ResourceOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeNull();
    }

    [Fact]
    public void GrantInvoke_OwnedRoleInEnvAgnosticStacks_WritesIdentityPolicy()
    {
        var app = new App();
        var stack = new Stack(app, "EnvAgnostic");
        _ = CreateFunction(stack, out var endpoint);
        var role = OwnedRole(stack);

        var act = () => endpoint.GrantInvoke(role);

        act.Should().NotThrow();
        ResourceOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeNull();
    }

    [Fact]
    public void GrantInvoke_ImportedMutableSameAccountRole_WritesAttachedIdentityPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Imported", SameAccountRoleArn);

        endpoint.GrantInvoke(imported);

        var template = TemplateJson(stack);
        ResourceOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeNull();
        var policies = ResourcesOfType(template, "AWS::IAM::Policy");
        policies.Should().HaveCount(1);
        var document = JsonSerializer.Serialize(policies[0].GetProperty("Properties"));
        document.Should().Contain("lambda:InvokeWebFunctionEndpoint");
    }

    [Fact]
    public void GrantInvoke_ImportedImmutableSameAccountRole_WritesResourceStatementOnly()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Imported", SameAccountRoleArn, new FromRoleArnOptions { Mutable = false });

        endpoint.GrantInvoke(imported);

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        var statements = ResourcePolicyStatements(template);
        statements.GetArrayLength().Should().Be(1);
        statements[0].GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
    }

    [Fact]
    public void GrantInvoke_ImportedUnresolvedRoleAutomatic_ThrowsAndWritesNoIdentityPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Imported", Fn.ImportValue("caller-role-arn"));

        var act = () => endpoint.GrantInvoke(imported);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be fully configured*");
        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().BeEmpty();
    }

    [Fact]
    public void GrantInvoke_ImportedUnresolvedRoleExternallyManaged_WritesResourceStatementAndAnnotation()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Imported", Fn.ImportValue("caller-role-arn"));

        endpoint.GrantInvoke(imported, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        var statements = ResourcePolicyStatements(template);
        statements.GetArrayLength().Should().Be(1);
        statements[0].GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
    }

    [Fact]
    public void GrantInvoke_DifferentLiteralAccountAutomatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Partner", OtherAccountRoleArn);

        var act = () => endpoint.GrantInvoke(imported);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_DifferentLiteralAccountExternallyManaged_WritesResourceStatementOnly()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var imported = Role.FromRoleArn(stack, "Partner", OtherAccountRoleArn);

        var grant = endpoint.GrantInvoke(imported, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        grant.PrincipalStatements.Should().BeEmpty();
        grant.ResourceStatements.Should().NotBeEmpty();

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        ResourcePolicyStatements(template).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GrantInvoke_ServicePrincipalAutomatic_WritesResourceStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var service = new ServicePrincipal("cloudtrail.amazonaws.com");

        endpoint.GrantInvoke(service);

        var statements = ResourcePolicyStatements(TemplateJson(stack));
        statements.GetArrayLength().Should().Be(1);
        statements[0].GetProperty("Principal").GetProperty("Service").GetString()
            .Should().Be("cloudtrail.amazonaws.com");
        statements[0].GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
    }

    [Fact]
    public void GrantInvoke_AccountPrincipalAutomatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var account = new AccountPrincipal("999999999999");

        var act = () => endpoint.GrantInvoke(account);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_ArnPrincipalExternallyManaged_WritesResourceStatementOnly()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var arn = new ArnPrincipal("arn:aws:iam::999999999999:role/x");

        var grant = endpoint.GrantInvoke(arn, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        grant.PrincipalStatements.Should().BeEmpty();
        ResourcePolicyStatements(TemplateJson(stack)).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GrantInvoke_OwnedRoleExternallyManaged_WritesResourceStatementOnly()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var role = OwnedRole(stack);

        var grant = endpoint.GrantInvoke(role, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        grant.PrincipalStatements.Should().BeEmpty();

        var template = TemplateJson(stack);
        ResourceByLogicalIdPrefix(template, "CallerRole")
            .GetProperty("Properties").TryGetProperty("Policies", out _).Should().BeFalse();
        ResourcePolicyStatements(template).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GrantInvoke_ApplicationManagedEndpoint_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint, WebFunctionAuthType.ApplicationManaged);
        var role = OwnedRole(stack);

        var act = () => endpoint.GrantInvoke(role);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_IsScopedToEachEndpointArn()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var function = CreateFunction(stack, out var defaultEndpoint);
        var second = function.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        defaultEndpoint.GrantInvoke(new ServicePrincipal("a.amazonaws.com"));
        second.GrantInvoke(new ServicePrincipal("b.amazonaws.com"));

        var template = TemplateJson(stack);
        var statements = ResourcePolicyStatements(template);
        statements.GetArrayLength().Should().Be(2);

        var endpointIds = Resources(template).EnumerateObject()
            .Where(property => property.Value.GetProperty("Type").GetString() == "AWS::Lambda::WebFunctionEndpoint")
            .Select(property => property.Name)
            .ToList();
        endpointIds.Should().HaveCount(2);

        var scopedIds = statements.EnumerateArray()
            .Select(statement => statement.GetProperty("Resource").GetProperty("Fn::GetAtt")[0].GetString())
            .ToList();
        scopedIds.Should().BeEquivalentTo(endpointIds);
    }

    [Fact]
    public void GrantInvoke_StatementNotAdded_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var fake = new FakeResourceWithPolicy(stack, "fake-resource", statementAdded: false);
        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = ["arn:aws:lambda:us-east-1:123456789012:web-function/x/endpoint/default"],
            Principals = [role.GrantPrincipal],
        });

        var act = () => WebFunctionGrants.AddResourceStatement(fake, role, role.GrantPrincipal, statement.Resources[0], "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromEndpointAttributes_ExposesConfiguration()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = EndpointArn,
            DomainName = "abc123.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        endpoint.EndpointArn.Should().Be(EndpointArn);
        endpoint.DomainName.Should().Be("abc123.cloudfront.net");
        endpoint.Url.Should().Be("https://abc123.cloudfront.net");
        endpoint.AuthType.Should().Be(WebFunctionAuthType.IamAuth);
        endpoint.EndpointType.Should().Be(WebFunctionEndpointType.HomeRegion);
        endpoint.EndpointName.Should().Be("default");
    }

    [Fact]
    public void FromEndpointAttributes_OwnsNothingAndWritesIdentityPolicyForEstablishedGrantee()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:123456789012:web-function/shared/endpoint/default",
            DomainName = "abc123.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });
        var role = OwnedRole(stack);

        endpoint.GrantInvoke(role);

        var template = TemplateJson(stack);
        ResourceOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeNull();
    }

    [Fact]
    public void FromEndpointAttributes_GrantCloudFrontInvoke_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = EndpointArn,
            DomainName = "abc123.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var act = () => endpoint.GrantCloudFrontInvoke("arn:aws:cloudfront::123456789012:distribution/E1111111111111");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromEndpointAttributes_ExternallyManagedGrant_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = EndpointArn,
            DomainName = "abc123.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });
        var imported = Role.FromRoleArn(stack, "Partner", OtherAccountRoleArn);

        var act = () => endpoint.GrantInvoke(imported, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromEndpointAttributes_TokenArnWithUnresolvedRoleAutomatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.ImportValue("shared-endpoint-arn"),
            DomainName = "abc123.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });
        var imported = Role.FromRoleArn(stack, "Imported", Fn.ImportValue("caller-role-arn"));

        var act = () => endpoint.GrantInvoke(imported);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Endpoint_ImplementsIWebFunctionEndpoint()
    {
        typeof(IWebFunctionEndpoint).IsAssignableFrom(typeof(WebFunctionEndpointConstruct)).Should().BeTrue();
    }
}

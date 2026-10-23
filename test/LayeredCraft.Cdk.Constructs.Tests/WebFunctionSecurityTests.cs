using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using Constructs;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionSecurityTests
{
    private const string SameAccountRoleArn = "arn:aws:iam::123456789012:role/caller";
    private const string LiteralEndpointArn = "arn:aws:lambda:us-east-1:123456789012:web-function/shared/endpoint/default";

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

    private static WebFunctionConstruct CreateFunction(
        Stack stack,
        out WebFunctionEndpointConstruct defaultEndpoint,
        WebFunctionAuthType authType = WebFunctionAuthType.IamAuth,
        string functionId = "web-function")
    {
        var artifacts = Bucket.FromBucketName(stack, $"{functionId}-artifacts", "artifacts");
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

    // ----- Service principals -----

    [Fact]
    public void GrantInvoke_ServicePrincipal_Automatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new ServicePrincipal("cloudtrail.amazonaws.com"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddToResourcePolicy*");
    }

    [Fact]
    public void GrantInvoke_ServicePrincipal_ExternallyManaged_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(
            new ServicePrincipal("cloudtrail.amazonaws.com"),
            new WebFunctionInvokeGrantOptions { CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_CloudFrontServicePrincipal_AdvisesGrantCloudFrontInvoke()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new ServicePrincipal("cloudfront.amazonaws.com"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GrantCloudFrontInvoke*");
    }

    [Fact]
    public void GrantInvoke_ConditionedServicePrincipal_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var principal = new PrincipalWithConditions(
            new ServicePrincipal("cloudtrail.amazonaws.com"),
            new Dictionary<string, object> { ["StringEquals"] = new Dictionary<string, object> { ["aws:SourceAccount"] = "123456789012" } });

        var act = () => endpoint.GrantInvoke(principal);

        act.Should().Throw<InvalidOperationException>();
    }

    // ----- Wildcard principals -----

    [Fact]
    public void GrantInvoke_AnyPrincipal_Automatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new AnyPrincipal());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_StarPrincipal_ExternallyManaged_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(
            new StarPrincipal(),
            new WebFunctionInvokeGrantOptions { CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_ConditionedOrganizationPrincipal_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var principal = new PrincipalWithConditions(
            new OrganizationPrincipal("o-abcdef1234"),
            new Dictionary<string, object> { ["StringEquals"] = new Dictionary<string, object> { ["aws:PrincipalOrgID"] = "o-abcdef1234" } });

        var act = () => endpoint.GrantInvoke(principal);

        act.Should().Throw<InvalidOperationException>();
    }

    // ----- Account principals -----

    [Fact]
    public void GrantInvoke_AccountPrincipal_SameAccount_Automatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new AccountPrincipal("123456789012"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GrantInvoke_AccountRootPrincipal_SameAccount_Automatic_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new AccountRootPrincipal());

        act.Should().Throw<InvalidOperationException>();
    }

    // ----- AddToResourcePolicy statement restrictions -----

    [Fact]
    public void AddToResourcePolicy_UnconditionedWildcardAllow_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new AnyPrincipal()],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddToResourcePolicy_AllowWithNotPrincipal_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            NotPrincipals = [new AnyPrincipal()],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddToResourcePolicy_DenyWildcard_IsAccepted()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new AnyPrincipal()],
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().NotThrow();
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
    }

    [Fact]
    public void AddToResourcePolicy_ConditionedWildcardAllow_IsAccepted()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new AnyPrincipal()],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["aws:PrincipalOrgID"] = "o-abcdef1234" },
            },
        });

        var act = () => endpoint.AddToResourcePolicy(statement);

        act.Should().NotThrow();
        var policy = ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Single();
        var statements = policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");
        statements.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AddToResourcePolicy_PrincipalLessStatement_FailsEagerValidation()
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

        act.Should().Throw<InvalidOperationException>();
    }

    // ----- Reserved ids / read-only endpoints -----

    [Theory]
    [InlineData("Function")]
    [InlineData("Revision")]
    [InlineData("DefaultEndpoint")]
    [InlineData("ExecutionRole")]
    [InlineData("LogGroup")]
    [InlineData("ResourcePolicy")]
    public void AddEndpoint_ReservedId_Throws(string id)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);

        var act = () => construct.AddEndpoint(id, new WebFunctionEndpointOptions
        {
            EndpointName = $"ep-{id}",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Endpoints_CannotBeCastToMutableList()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        construct.AddEndpoint("public", new WebFunctionEndpointOptions
        {
            EndpointName = "public",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        construct.Endpoints.Should().BeAssignableTo<IReadOnlyList<WebFunctionEndpointConstruct>>();
        (construct.Endpoints is List<WebFunctionEndpointConstruct>).Should().BeFalse();
        (construct.Endpoints is ReadOnlyCollection<WebFunctionEndpointConstruct>).Should().BeTrue();
        construct.Endpoints.Should().HaveCount(2);
    }

    // ----- Diagnostics -----

    [Fact]
    public void GrantInvoke_UnestablishedEndpoint_ErrorMessageNamesEndpointAndGranteeWithoutRawToken()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var imported = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.Ref("EndpointArn"),
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var role = OwnedRole(stack);

        var act = () => imported.GrantInvoke(role);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("imported");
        exception.Message.Should().Contain("CallerRole");
        exception.Message.Should().Contain("endpoint's account cannot be established");
        exception.Message.Should().NotContain("${Token[");
        exception.Message.Should().NotContain("<unresolved>");
    }

    // ----- Imported endpoint validation / environment -----

    [Fact]
    public void FromEndpointAttributes_InvalidArn_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = "not-an-arn",
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromEndpointAttributes_EmptyDomainName_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = LiteralEndpointArn,
            DomainName = "",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromEndpointAttributes_NonTwelveDigitAccount_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = LiteralEndpointArn,
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
            Account = "123",
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromEndpointAttributes_MismatchedAccount_Throws()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = LiteralEndpointArn,
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
            Account = "999999999999",
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromEndpointAttributes_UsesEndpointEnvironment()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = LiteralEndpointArn,
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        endpoint.Env.Account.Should().Be("123456789012");
        endpoint.Env.Region.Should().Be("us-east-1");
    }

    [Fact]
    public void FromEndpointAttributes_TokenArnWithoutAccount_ConstructsAndGrantThrows()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.Ref("EndpointArn"),
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var role = OwnedRole(stack);
        var act = () => endpoint.GrantInvoke(role);

        act.Should().Throw<InvalidOperationException>();
        TemplateJson(stack).GetProperty("Resources").EnumerateObject()
            .Any(p => p.Value.GetProperty("Type").GetString() == "AWS::IAM::Policy")
            .Should().BeFalse();
    }

    [Fact]
    public void FromEndpointAttributes_TokenArnWithMatchingAccount_WritesIdentityForOwnedCaller()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var endpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = Fn.Ref("EndpointArn"),
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
            Account = "123456789012",
        });

        var role = OwnedRole(stack);
        endpoint.GrantInvoke(role);

        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().HaveCount(1);
    }

    // ----- Dependency cycles -----

    [Fact]
    public void SelfInvocationGrant_Synthesizes()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out var endpoint);

        endpoint.GrantInvoke(construct);

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::IAM::Policy", 1);
        template.ResourceCountIs("AWS::Lambda::ResourcePolicy", 0);

        // The identity statement must be scoped to this endpoint's ARN.
        var statement = ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy")[0]
            .GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement");
        statement[0].GetProperty("Action").GetString().Should().Be("lambda:InvokeWebFunctionEndpoint");
        statement[0].GetProperty("Resource").GetProperty("Fn::GetAtt")[1].GetString().Should().Be("EndpointArn");
    }

    [Fact]
    public void MutualInvocationGrant_Synthesizes()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var first = CreateFunction(stack, out var firstEndpoint);
        var second = CreateFunction(stack, out var secondEndpoint, functionId: "web-function-two");

        firstEndpoint.GrantInvoke(second);
        secondEndpoint.GrantInvoke(first);

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::IAM::Policy", 2);
        template.ResourceCountIs("AWS::Lambda::ResourcePolicy", 0);
    }

    [Fact]
    public void GrantInvoke_DifferentStacksSameLiteralAccount_WritesIdentityPolicy()
    {
        var app = new App();
        var endpointStack = new Stack(app, "EndpointStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });
        var callerStack = new Stack(app, "CallerStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });

        _ = CreateFunction(endpointStack, out var endpoint);
        var role = new Role(callerStack, "CallerRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });

        endpoint.GrantInvoke(role);

        Template.FromStack(callerStack).ResourceCountIs("AWS::IAM::Policy", 1);
        Template.FromStack(endpointStack).ResourceCountIs("AWS::Lambda::ResourcePolicy", 0);
    }

    [Fact]
    public void GrantInvoke_DifferentStacksUnresolvedAccount_Throws()
    {
        var app = new App();
        var endpointStack = new Stack(app, "EndpointStack");
        var callerStack = new Stack(app, "CallerStack");

        _ = CreateFunction(endpointStack, out var endpoint);
        var role = new Role(callerStack, "CallerRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });

        var act = () => endpoint.GrantInvoke(role);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different stack*");
    }

    [Fact]
    public void GrantInvoke_DifferentStacksMismatchedLiteralAccount_ReportsMismatch()
    {
        var app = new App();
        var endpointStack = new Stack(app, "EndpointStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });
        var callerStack = new Stack(app, "CallerStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "111111111111", Region = "us-east-1" },
        });

        _ = CreateFunction(endpointStack, out var endpoint);
        var role = new Role(callerStack, "CallerRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });

        var act = () => endpoint.GrantInvoke(role);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*matching literal accounts*");
    }

    [Fact]
    public void GrantInvoke_ImportedRoleOnImportedEndpoint_ThrowsWithoutPolicyMutation()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var importedEndpoint = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "imported", new WebFunctionEndpointAttributes
        {
            EndpointArn = LiteralEndpointArn,
            DomainName = "abc.cloudfront.net",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        var importedRole = Role.FromRoleArn(stack, "ImportedCaller", SameAccountRoleArn);

        var act = () => importedEndpoint.GrantInvoke(importedRole);

        act.Should().Throw<InvalidOperationException>();

        var json = JsonSerializer.Serialize(Template.FromStack(stack).ToJSON());
        json.Should().NotContain("AWS::IAM::Policy");
        json.Should().NotContain("AWS::Lambda::ResourcePolicy");
    }

    // ----- CDK-upgrade guard (real-CDK principal behaviour observed on Amazon.CDK.Lib 2.274.0) -----
    // These tests pin the observed identity-write behaviour of real CDK principals. They are not a CDK
    // contract: any Amazon.CDK.Lib upgrade that changes them MUST fail this group and block the upgrade
    // until the WebFunctionGrants account/identity logic is re-validated against the new behaviour.

    [Fact]
    public void CdkUpgradeGuard_OwnedRole_WritesIdentityPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantInvoke(OwnedRole(stack));

        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().HaveCount(1);
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void CdkUpgradeGuard_MutableImportedSameAccountRole_UsesResourcePolicyFallback()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantInvoke(Role.FromRoleArn(stack, "Caller", SameAccountRoleArn));

        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
    }

    [Fact]
    public void CdkUpgradeGuard_ImmutableImportedSameAccountRole_FallsBackToResourcePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantInvoke(Role.FromRoleArn(stack, "Caller", SameAccountRoleArn, new FromRoleArnOptions { Mutable = false }));

        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
    }

    [Fact]
    public void CdkUpgradeGuard_ImmutableWithAddGrantsToResources_NeverSilentlySucceeds()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        endpoint.GrantInvoke(Role.FromRoleArn(
            stack,
            "Caller",
            SameAccountRoleArn,
            new FromRoleArnOptions { Mutable = false, AddGrantsToResources = true }));

        // Never a silent no-op: authorization is provided by exactly one resource-policy statement.
        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
    }

    [Fact]
    public void CdkUpgradeGuard_OwnedRoleWithoutPolicyUpdates_NeverSilentlySucceeds()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var role = OwnedRole(stack);
        endpoint.GrantInvoke(role.WithoutPolicyUpdates());

        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().HaveCount(1);
    }

    [Fact]
    public void CdkUpgradeGuard_WithConditionsWrappers_RejectedWithoutPolicies()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var role = OwnedRole(stack);

        var conditions = new Dictionary<string, object>
        {
            ["Bool"] = new Dictionary<string, object> { ["aws:SecureTransport"] = "true" },
        };

        var accountWrapped = new AccountPrincipal("123456789012").WithConditions(conditions);
        var arnWrapped = new ArnPrincipal(role.RoleArn).WithConditions(conditions);

        // jsii must materialize both WithConditions results as PrincipalWithConditions; if a future CDK upgrade
        // changes this, the account-principal bypass could reopen and this guard fails.
        accountWrapped.GetType().FullName.Should().Be("Amazon.CDK.AWS.IAM.PrincipalWithConditions");
        arnWrapped.GetType().FullName.Should().Be("Amazon.CDK.AWS.IAM.PrincipalWithConditions");
        accountWrapped.Should().BeAssignableTo<PrincipalWithConditions>();
        arnWrapped.Should().BeAssignableTo<PrincipalWithConditions>();

        var automatic = new Action[]
        {
            () => endpoint.GrantInvoke(accountWrapped),
            () => endpoint.GrantInvoke(arnWrapped),
        };
        var externallyManaged = new Action[]
        {
            () => endpoint.GrantInvoke(
                accountWrapped,
                new WebFunctionInvokeGrantOptions { CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged }),
            () => endpoint.GrantInvoke(
                arnWrapped,
                new WebFunctionInvokeGrantOptions { CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged }),
        };

        foreach (var act in automatic)
            act.Should().Throw<InvalidOperationException>();
        foreach (var act in externallyManaged)
            act.Should().Throw<InvalidOperationException>();

        // Rejected before any authorization mutation: synthesize and assert nothing was written.
        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void GrantInvoke_PrincipalWithConditionsAroundOwnedRole_IsRejectedWithoutPolicies()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var wrapped = new PrincipalWithConditions(
            OwnedRole(stack).GrantPrincipal,
            new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string> { ["aws:PrincipalOrgID"] = "o-abcdef1234" },
            });

        var act = () => endpoint.GrantInvoke(wrapped);

        act.Should().Throw<InvalidOperationException>();
        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void GrantInvoke_PrincipalWithConditionsAroundAccountPrincipal_IsRejectedWithoutPolicies()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var wrapped = new PrincipalWithConditions(
            new AccountPrincipal("123456789012"),
            new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string> { ["aws:PrincipalOrgID"] = "o-abcdef1234" },
            });

        var act = () => endpoint.GrantInvoke(wrapped);

        act.Should().Throw<InvalidOperationException>();
        var template = TemplateJson(stack);
        ResourcesOfType(template, "AWS::IAM::Policy").Should().BeEmpty();
        ResourcesOfType(template, "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void GrantInvoke_AccountRootPrincipal_Automatic_ThrowsWithoutResourcePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var act = () => endpoint.GrantInvoke(new AccountRootPrincipal());

        act.Should().Throw<InvalidOperationException>();
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void AddToResourcePolicy_RejectedStatement_LeavesNoResourcePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var bad = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new AnyPrincipal()],
        });

        var act = () => endpoint.AddToResourcePolicy(bad);

        act.Should().Throw<ArgumentException>();
        ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy").Should().BeEmpty();
    }

    [Fact]
    public void AddToResourcePolicy_StoresFrozenCopy_UnaffectedByLaterMutation()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new ArnPrincipal(SameAccountRoleArn)],
        });

        endpoint.AddToResourcePolicy(statement);
        statement.AddCondition(
            "StringEquals",
            new Dictionary<string, string> { ["aws:SourceAccount"] = "999999999999" });

        var policies = ResourcesOfType(TemplateJson(stack), "AWS::Lambda::ResourcePolicy");
        policies.Should().HaveCount(1);
        JsonSerializer.Serialize(policies[0]).Should().NotContain("999999999999");
    }

    [Fact]
    public void GrantInvoke_ExternallyManaged_EmitsAccurateAnnotation()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);

        var partner = Role.FromRoleArn(stack, "Partner", "arn:aws:iam::222233334444:role/partner");
        endpoint.GrantInvoke(partner, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        var annotations = endpoint.Node.Metadata
            .Select(entry => entry.Data?.ToString() ?? string.Empty)
            .ToList();

        annotations.Should().Contain(text =>
            text.Contains("requires an identity policy", StringComparison.Ordinal)
            && text.Contains("lambda:InvokeWebFunctionEndpoint", StringComparison.Ordinal)
            && !text.Contains("${Token[", StringComparison.Ordinal));
    }

    [Fact]
    public void GrantInvoke_BffApiCrossReference_SynthesizesWithoutCycle()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var api = CreateFunction(stack, out var apiEndpoint, functionId: "api-function");
        var web = CreateFunction(stack, out var webEndpoint, functionId: "web-function");

        apiEndpoint.GrantInvoke(web.Role);
        webEndpoint.GrantInvoke(api.Role);

        // Must synthesize without a dependency cycle between the two functions.
        Template.FromStack(stack).Should().NotBeNull();

        ResourcesOfType(TemplateJson(stack), "AWS::IAM::Policy").Should().HaveCount(2);
    }
}

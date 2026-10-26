using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using LogGroup = Amazon.CDK.AWS.Logs.LogGroup;
using LogGroupProps = Amazon.CDK.AWS.Logs.LogGroupProps;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

namespace LayeredCraft.Cdk.Constructs.Tests.Testing;

/// <summary>
/// Verifies the consumer-facing <see cref="WebFunctionConstructAssertions"/> and
/// <see cref="WebFunctionConstructPropsBuilder"/> helpers, including the negative cases that
/// ensure a wrong expectation is actually detected.
/// </summary>
[Collection("CDK Tests")]
public class WebFunctionConstructTestingHelpersTests
{
    private const string ArtifactsBucketName = "test-web-function-artifacts";
    private const string CloudFrontDistributionArn = "arn:aws:cloudfront::123456789012:distribution/E1111111111111";
    private const string PartnerRoleArn = "arn:aws:iam::222233334444:role/partner";

    private static WebFunctionConstruct CreateFunction(
        Stack stack,
        out WebFunctionEndpointConstruct endpoint,
        string functionName = "test-web-function",
        string id = "web-function",
        string codeKey = "artifacts/app.zip")
    {
        var artifacts = Bucket.FromBucketName(stack, id + "Artifacts", ArtifactsBucketName);
        var construct = new WebFunctionConstruct(stack, id, new WebFunctionConstructProps
        {
            FunctionName = functionName,
            Code = WebFunctionCode.FromBucket(
                artifacts,
                codeKey,
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });
        endpoint = construct.DefaultEndpoint;
        return construct;
    }

    private static WebFunctionConstruct CreateOwnedBucketFunction(Stack stack, string functionName = "test-web-function")
    {
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        return new WebFunctionConstruct(stack, "web-function", new WebFunctionConstructProps
        {
            FunctionName = functionName,
            Code = WebFunctionCode.FromBucket(artifacts, "app.zip"),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });
    }

    private static IOrigin AddOacDistribution(Stack stack, WebFunctionEndpointConstruct endpoint)
    {
        var origin = WebFunctionOrigin.WithOriginAccessControl(endpoint);
        _ = new Distribution(stack, "distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions { Origin = origin },
        });
        return origin;
    }

    // ---- ShouldHaveWebFunction -------------------------------------------------

    [Fact]
    public void ShouldHaveWebFunction_ShouldPassForMatchingName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunction("test-web-function");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveWebFunction_ShouldThrowForUnknownName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunction("other-function");

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveRevision ----------------------------------------------------

    [Fact]
    public void ShouldHaveRevision_ShouldDefaultToNodeJs24X()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveRevision("test-web-function");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveRevision_ShouldThrowForWrongRuntime()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveRevision("test-web-function", "python3.12");

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveRevisionCode ------------------------------------------------

    [Fact]
    public void ShouldHaveRevisionCode_ShouldMatchImportedBucketAndKey()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveRevisionCode(ArtifactsBucketName, "artifacts/app.zip");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveRevisionCode_ShouldThrowForWrongKey()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveRevisionCode(ArtifactsBucketName, "artifacts/other.zip");

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveEndpoint ----------------------------------------------------

    [Fact]
    public void ShouldHaveEndpoint_ShouldMatchNameAndAuthType()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack)
            .ShouldHaveEndpoint("test-web-function", "default", WebFunctionAuthType.IamAuth);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveEndpoint_ShouldThrowForWrongAuthType()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack)
            .ShouldHaveEndpoint("test-web-function", "default", WebFunctionAuthType.ApplicationManaged);

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveEndpointScaling ---------------------------------------------

    [Fact]
    public void ShouldHaveEndpointScaling_ShouldAssertAbsentScalingWhenNull()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveEndpointScaling("default", null, null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveEndpointScaling_ShouldMatchConfiguredValues()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        construct.AddEndpoint("scaled", new WebFunctionEndpointOptions
        {
            EndpointName = "scaled",
            AuthType = WebFunctionAuthType.IamAuth,
            MaxEnvironments = 5,
            RateLimit = 100,
        });

        var act = () => Template.FromStack(stack).ShouldHaveEndpointScaling("scaled", 5, 100);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveEndpointScaling_ShouldThrowForWrongValue()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out _);
        construct.AddEndpoint("scaled", new WebFunctionEndpointOptions
        {
            EndpointName = "scaled",
            AuthType = WebFunctionAuthType.IamAuth,
            MaxEnvironments = 5,
            RateLimit = 100,
        });

        var act = () => Template.FromStack(stack).ShouldHaveEndpointScaling("scaled", 6, 100);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHaveEndpointScaling_ShouldThrowForUnknownEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveEndpointScaling("missing", 1, 1);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- ShouldHaveInvokeGrant -------------------------------------------------

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldMatchIdentityPolicyStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out var endpoint);
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        endpoint.GrantInvoke(caller);

        var act = () => Template.FromStack(stack).ShouldHaveInvokeGrant(construct.FunctionName, "default");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldThrowWhenNoGrantExists()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveInvokeGrant("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- Resource policy helpers -----------------------------------------------

    [Fact]
    public void ShouldHaveNoResourcePolicy_ShouldPassWhenNoStatementsExist()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveNoResourcePolicy();

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveNoResourcePolicy_ShouldThrowWhenStatementExists()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.GrantCloudFrontInvoke(CloudFrontDistributionArn);

        var act = () => Template.FromStack(stack).ShouldHaveNoResourcePolicy();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHaveSingleResourcePolicy_ShouldPassForOnePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.GrantCloudFrontInvoke(CloudFrontDistributionArn);

        var act = () => Template.FromStack(stack).ShouldHaveSingleResourcePolicy("test-web-function");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveSingleResourcePolicy_ShouldThrowWhenNoPolicyExists()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveSingleResourcePolicy("test-web-function");

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- ShouldAllowCloudFront / principal -------------------------------------

    [Fact]
    public void ShouldAllowCloudFront_ShouldMatchCloudFrontStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.GrantCloudFrontInvoke(CloudFrontDistributionArn);

        var act = () => Template.FromStack(stack).ShouldAllowCloudFront("test-web-function", "default");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldAllowCloudFront_ShouldThrowWithoutCloudFrontStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldAllowCloudFront("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldAllowResourcePolicyPrincipal_ShouldMatchConfiguredPrincipal()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var partner = Role.FromRoleArn(stack, "Partner", PartnerRoleArn);
        endpoint.GrantInvoke(partner, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        var act = () => Template.FromStack(stack)
            .ShouldAllowResourcePolicyPrincipal("test-web-function", "default", PartnerRoleArn);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldAllowResourcePolicyPrincipal_ShouldThrowForOtherPrincipal()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var partner = Role.FromRoleArn(stack, "Partner", PartnerRoleArn);
        endpoint.GrantInvoke(partner, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        var act = () => Template.FromStack(stack)
            .ShouldAllowResourcePolicyPrincipal("test-web-function", "default", "arn:aws:iam::999999999999:role/other");

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- ShouldHaveWebFunctionOac ----------------------------------------------

    [Fact]
    public void ShouldHaveWebFunctionOac_ShouldMatchSigV4ByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        AddOacDistribution(stack, endpoint);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunctionOac();

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveWebFunctionOac_ShouldThrowForWrongProtocol()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        AddOacDistribution(stack, endpoint);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunctionOac("sigv4a");

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveCodeBucketReadStatement -------------------------------------

    [Fact]
    public void ShouldHaveCodeBucketReadStatement_ShouldMatchOwnedBucketPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateOwnedBucketFunction(stack);

        var act = () => Template.FromStack(stack).ShouldHaveCodeBucketReadStatement("test-web-function");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveCodeBucketReadStatement_ShouldThrowForUnknownFunction()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateOwnedBucketFunction(stack);

        var act = () => Template.FromStack(stack).ShouldHaveCodeBucketReadStatement("missing");

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- WebFunctionConstructPropsBuilder --------------------------------------

    [Fact]
    public void CdkTestHelper_CreateWebFunctionPropsBuilder_ShouldReturnBuilder()
    {
        CdkTestHelper.CreateWebFunctionPropsBuilder().Should().NotBeNull();
    }

    [Fact]
    public void Build_ShouldThrowWithoutCode()
    {
        var builder = CdkTestHelper.CreateWebFunctionPropsBuilder().WithIamAuthEndpoint();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*WithCode or WithTestArtifact*");
    }

    [Fact]
    public void Build_ShouldThrowWithoutEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var builder = CdkTestHelper.CreateWebFunctionPropsBuilder().WithTestArtifact(stack);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*WithEndpoint*");
    }

    [Fact]
    public void Build_ShouldApplyDefaults()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .Build();

        props.FunctionName.Should().Be("test-web-function");
        props.Endpoint.AuthType.Should().Be(WebFunctionAuthType.IamAuth);
        props.Runtime.Name.Should().Be("nodejs24.x");
        props.Timeout.ToSeconds().Should().Be(30);
    }

    [Fact]
    public void Build_ShouldFlowConfiguredValues()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithFunctionName("custom-function")
            .WithTestArtifact(stack, "artifacts/custom.zip")
            .WithApplicationManagedEndpoint()
            .WithEnvironmentVariable("KEY", "VALUE")
            .WithEntryPoint("server.js")
            .WithTimeout(Duration.Seconds(60))
            .WithMaxConcurrencyPerEnvironment(7)
            .WithDescription("custom description")
            .Build();

        props.FunctionName.Should().Be("custom-function");
        props.Endpoint.AuthType.Should().Be(WebFunctionAuthType.ApplicationManaged);
        props.EntryPoint.Should().Be("server.js");
        props.EnvironmentVariables["KEY"].Should().Be("VALUE");
        props.Timeout.ToSeconds().Should().Be(60);
        props.MaxConcurrencyPerEnvironment.Should().Be(7);
        props.Description.Should().Be("custom description");
    }

    [Fact]
    public void Build_ShouldCarrySuppliedRoleAndLogs()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = new Role(stack, "SuppliedRole", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        var logs = WebFunctionLogs.ServiceDefault();

        var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .WithRole(role)
            .WithLogs(logs)
            .Build();

        props.Role.Should().BeSameAs(role);
        props.Logs.Should().BeSameAs(logs);
    }

    [Fact]
    public void WithTestArtifact_ShouldSynthesizeWithoutCredentialsOrBucketResource()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .Build();

        _ = new WebFunctionConstruct(stack, "web", props);

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("test-web-function");
        template.ResourceCountIs("AWS::S3::Bucket", 0);
    }

    [Fact]
    public void WithEndpoint_ShouldSetWholeOptionsRecord()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var options = new WebFunctionEndpointOptions
        {
            EndpointName = "custom",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.MultiRegion,
            Regions = ["us-west-2"],
        };

        var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithTestArtifact(stack)
            .WithEndpoint(options)
            .Build();

        props.Endpoint.Should().BeSameAs(options);
    }

    // ---- D1: endpoint lookup is function-specific ------------------------------

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldNotBeSatisfiedByAnotherFunctionsEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _, functionName: "function-a", id: "a");
        _ = CreateFunction(stack, out var otherEndpoint, functionName: "function-b", id: "b");
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        otherEndpoint.GrantInvoke(caller);

        var template = Template.FromStack(stack);
        var matching = () => template.ShouldHaveInvokeGrant("function-b", "default");
        var unrelated = () => template.ShouldHaveInvokeGrant("function-a", "default");

        matching.Should().NotThrow();
        unrelated.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldAllowCloudFront_ShouldNotBeSatisfiedByAnotherFunctionsEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _, functionName: "function-a", id: "a");
        _ = CreateFunction(stack, out var otherEndpoint, functionName: "function-b", id: "b");
        otherEndpoint.GrantCloudFrontInvoke(CloudFrontDistributionArn);

        var template = Template.FromStack(stack);
        var matching = () => template.ShouldAllowCloudFront("function-b", "default");
        var unrelated = () => template.ShouldAllowCloudFront("function-a", "default");

        matching.Should().NotThrow();
        unrelated.Should().Throw<InvalidOperationException>();
    }

    // ---- D2: code-bucket statement is function-specific ------------------------

    [Fact]
    public void ShouldHaveCodeBucketReadStatement_ShouldNotBeSatisfiedByAnotherFunctionsPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateOwnedBucketFunction(stack, "function-a");
        _ = CreateFunction(stack, out _, functionName: "function-b", id: "b");

        var template = Template.FromStack(stack);
        var matching = () => template.ShouldHaveCodeBucketReadStatement("function-a");
        var unrelated = () => template.ShouldHaveCodeBucketReadStatement("function-b");

        matching.Should().NotThrow();
        unrelated.Should().Throw<InvalidOperationException>();
    }

    // ---- D3: IAM policy minimization and array resources -----------------------

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldMatchMinimizedArrayResourceStatement()
    {
        // Feature flags are read from the App context and must be supplied before any construct is created.
        var app = new App(new AppProps
        {
            Context = new Dictionary<string, object> { ["@aws-cdk/aws-iam:minimizePolicies"] = "true" },
        });
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });

        var construct = CreateFunction(stack, out var first);
        var second = construct.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
        });
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        first.GrantInvoke(caller);
        second.GrantInvoke(caller);

        var template = Template.FromStack(stack);
        var firstGrant = () => template.ShouldHaveInvokeGrant("test-web-function", "default");
        var secondGrant = () => template.ShouldHaveInvokeGrant("test-web-function", "second");

        firstGrant.Should().NotThrow();
        secondGrant.Should().NotThrow();

        // Proves the feature flag actually minimized the two grants into a single statement whose
        // Resource is an array, so the array-resource path in the assertions is genuinely exercised.
        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Resource"] = Match.ArrayWith(new object[]
                        {
                            Match.ObjectLike(new Dictionary<string, object>()),
                            Match.ObjectLike(new Dictionary<string, object>()),
                        }),
                    }),
                }),
            }),
        }));
    }

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldMatchArrayResourceStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateFunction(stack, out var first);
        var second = construct.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
        });
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        caller.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [first.EndpointArn, second.EndpointArn],
        }));

        var template = Template.FromStack(stack);
        var firstGrant = () => template.ShouldHaveInvokeGrant("test-web-function", "default");
        var secondGrant = () => template.ShouldHaveInvokeGrant("test-web-function", "second");

        firstGrant.Should().NotThrow();
        secondGrant.Should().NotThrow();
    }

    // ---- D4: ambiguous endpoint scaling ----------------------------------------

    [Fact]
    public void ShouldHaveEndpointScaling_ShouldThrowWhenEndpointNameIsAmbiguous()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _, functionName: "function-a", id: "a");
        _ = CreateFunction(stack, out _, functionName: "function-b", id: "b");

        var act = () => Template.FromStack(stack).ShouldHaveEndpointScaling("default", null, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*");
    }

    // ---- D5: authorization semantics -------------------------------------------

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldRejectDenyStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        caller.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
        }));

        var act = () => Template.FromStack(stack).ShouldHaveInvokeGrant("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHaveInvokeGrant_ShouldRejectUnrelatedAction()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        var caller = new Role(stack, "Caller", new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });
        caller.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["lambda:InvokeFunction"],
            Resources = [endpoint.EndpointArn],
        }));

        var act = () => Template.FromStack(stack).ShouldHaveInvokeGrant("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldAllowCloudFront_ShouldRejectDenyStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new ServicePrincipal("cloudfront.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["aws:SourceArn"] = CloudFrontDistributionArn },
            },
        }));

        var act = () => Template.FromStack(stack).ShouldAllowCloudFront("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldAllowCloudFront_ShouldRejectWrongActionStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["lambda:InvokeFunction"],
            Resources = [endpoint.EndpointArn],
            Principals = [new ServicePrincipal("cloudfront.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["aws:SourceArn"] = CloudFrontDistributionArn },
            },
        }));

        var act = () => Template.FromStack(stack).ShouldAllowCloudFront("test-web-function", "default");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldAllowResourcePolicyPrincipal_ShouldRejectDenyStatement()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out var endpoint);
        endpoint.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = ["lambda:InvokeWebFunctionEndpoint"],
            Resources = [endpoint.EndpointArn],
            Principals = [new ArnPrincipal(PartnerRoleArn)],
        }));

        var act = () => Template.FromStack(stack)
            .ShouldAllowResourcePolicyPrincipal("test-web-function", "default", PartnerRoleArn);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- D6: AutoData fixture is jsii-compatible -------------------------------

    [Theory]
    [WebFunctionConstructAutoData]
    public void WebFunctionConstructAutoData_ShouldSupplySynthesizableProps(WebFunctionConstructProps props)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new WebFunctionConstruct(stack, "web", props);

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("test-web-function");
        template.ShouldHaveEndpoint("test-web-function", "default", WebFunctionAuthType.IamAuth);
        template.ResourceCountIs("AWS::S3::Bucket", 0);
    }

    // ---- D7: artifact construct-id collisions ----------------------------------

    [Fact]
    public void WithTestArtifact_ShouldSupportMultipleFunctionsInOneStack()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var first = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithFunctionName("first-function")
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .Build();
        var second = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithFunctionName("second-function")
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .Build();

        _ = new WebFunctionConstruct(stack, "first", first);
        _ = new WebFunctionConstruct(stack, "second", second);

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("first-function");
        template.ShouldHaveWebFunction("second-function");
        template.ResourceCountIs("AWS::S3::Bucket", 0);
    }

    // ---- D8: builder state isolation -------------------------------------------

    [Fact]
    public void Build_ShouldIsolateEnvironmentDictionary()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var builder = CdkTestHelper.CreateWebFunctionPropsBuilder()
            .WithTestArtifact(stack)
            .WithIamAuthEndpoint()
            .WithEnvironmentVariable("KEY", "VALUE");

        var props = builder.Build();
        builder.WithEnvironmentVariable("LATE", "changed");

        props.EnvironmentVariables["KEY"].Should().Be("VALUE");
        props.EnvironmentVariables.ContainsKey("LATE").Should().BeFalse();
    }

    // ---- #104 companion log group assertions -----------------------------------

    private static WebFunctionConstruct CreateUseFunction(Stack stack, WebFunctionLogs logs, string id = "web-function")
    {
        var artifacts = Bucket.FromBucketName(stack, id + "Artifacts", ArtifactsBucketName);
        return new WebFunctionConstruct(stack, id, new WebFunctionConstructProps
        {
            FunctionName = "test-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "artifacts/app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Logs = logs,
        });
    }

    [Fact]
    public void ShouldHaveWebFunctionLogGroup_ShouldPassForCentralizedGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunctionLogGroup("/aws/lambda/web/test-web-function", 14);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveWebFunctionLogGroup_ShouldThrowForWrongRetention()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunctionLogGroup("/aws/lambda/web/test-web-function", 30);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldHaveWebFunctionLogGroup_ShouldThrowForMissingGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveWebFunctionLogGroup("/aws/lambda/web/other-function", 14);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldHaveCompanionLogGroup_ShouldPassForCompanionGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveCompanionLogGroup("/aws/lambda/web/test-web-function", 3);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveCompanionLogGroup_ShouldThrowForWrongRetention()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveCompanionLogGroup("/aws/lambda/web/test-web-function", 14);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldHaveCompanionLogGroup_ShouldThrowWhenCompanionMissing()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        _ = CreateUseFunction(stack, WebFunctionLogs.Use(imported));

        var act = () => Template.FromStack(stack).ShouldHaveCompanionLogGroup("/custom/logs", 3);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ShouldHaveRevisionDependingOnLogGroups_ShouldPassForManagedGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateFunction(stack, out _);

        var act = () => Template.FromStack(stack).ShouldHaveRevisionDependingOnLogGroups(
            "test-web-function",
            "/aws/lambda/web/test-web-function",
            "/aws/lambda/web/test-web-function.local");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveRevisionDependingOnLogGroups_ShouldThrowWhenDependencyMissing()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        _ = new LogGroup(stack, "Unrelated", new LogGroupProps { LogGroupName = "/unrelated/logs" });
        _ = CreateUseFunction(stack, WebFunctionLogs.Use(imported));

        // The group exists in the template but the revision does not depend on it.
        var act = () => Template.FromStack(stack).ShouldHaveRevisionDependingOnLogGroups(
            "test-web-function",
            "/unrelated/logs");

        act.Should().Throw<Exception>();
    }
}

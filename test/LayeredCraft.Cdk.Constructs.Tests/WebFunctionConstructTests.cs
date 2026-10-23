using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using DynamoAttribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionConstructTests
{
    private static JsonElement TemplateJson(Stack stack)
    {
        var template = Template.FromStack(stack);
        var json = JsonSerializer.Serialize(template.ToJSON());
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Resources(JsonElement template) => template.GetProperty("Resources");

    private static JsonElement ResourceById(JsonElement template, string logicalId)
        => Resources(template).GetProperty(logicalId);

    private static JsonElement? ResourceOfType(JsonElement template, string type)
    {
        foreach (var property in Resources(template).EnumerateObject())
        {
            if (property.Value.GetProperty("Type").GetString() == type)
                return property.Value;
        }

        return null;
    }

    private static string? LogicalIdOfType(JsonElement template, string type)
    {
        foreach (var property in Resources(template).EnumerateObject())
        {
            if (property.Value.GetProperty("Type").GetString() == type)
                return property.Name;
        }

        return null;
    }

    private static List<string> DependsOn(JsonElement resource)
    {
        if (!resource.TryGetProperty("DependsOn", out var dependsOn))
            return [];

        return dependsOn.ValueKind == JsonValueKind.Array
            ? dependsOn.EnumerateArray().Select(value => value.GetString()!).ToList()
            : [dependsOn.GetString()!];
    }

    private static CfnBucket CfnBucketOf(Bucket bucket) => (CfnBucket)bucket.Node.DefaultChild!;

    private static void SetOwnedBucketVersioningStatus(Bucket bucket, string status)
        => CfnBucketOf(bucket).VersioningConfiguration = new CfnBucket.VersioningConfigurationProperty { Status = status };

    private static void SetOwnedBucketEncryption(Bucket bucket, string algorithm, string? keyId)
        => CfnBucketOf(bucket).BucketEncryption = new CfnBucket.BucketEncryptionProperty
        {
            ServerSideEncryptionConfiguration = new object[]
            {
                new CfnBucket.ServerSideEncryptionRuleProperty
                {
                    ServerSideEncryptionByDefault = new CfnBucket.ServerSideEncryptionByDefaultProperty
                    {
                        SseAlgorithm = algorithm,
                        KmsMasterKeyId = keyId,
                    },
                },
            },
        };

    private static WebFunctionConstructProps DefaultProps(
        IBucket artifacts,
        WebFunctionCodeBucketPolicy bucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged)
        => new()
        {
            FunctionName = "test-web-function",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = bucketPolicy }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        };

    [Fact]
    public void Construct_ShouldCreateFunctionRevisionAndDefaultEndpointWithStableIds()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var construct = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        construct.FunctionName.Should().Be("test-web-function");
        construct.Role.Should().NotBeNull();
        construct.DefaultEndpoint.Should().NotBeNull();

        var template = TemplateJson(stack);
        LogicalIdOfType(template, "AWS::Lambda::WebFunction")!.Should().StartWith("webfunctionFunction");
        LogicalIdOfType(template, "AWS::Lambda::WebFunctionRevision")!.Should().StartWith("webfunctionRevision");
        LogicalIdOfType(template, "AWS::Lambda::WebFunctionEndpoint")!.Should()
            .StartWith("webfunctionDefaultEndpoint");
    }

    [Fact]
    public void Construct_ShouldUseLiteralFunctionNameOnAllResources()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var template = TemplateJson(stack);

        var function = ResourceOfType(template, "AWS::Lambda::WebFunction")!.Value;
        function.GetProperty("Properties").GetProperty("FunctionName").GetString().Should().Be("test-web-function");

        var revision = ResourceOfType(template, "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").GetProperty("FunctionName").GetString().Should().Be("test-web-function");

        var endpoint = ResourceOfType(template, "AWS::Lambda::WebFunctionEndpoint")!.Value;
        endpoint.GetProperty("Properties").GetProperty("FunctionName").GetString().Should().Be("test-web-function");
    }

    [Fact]
    public void Construct_ShouldWireDependsOnChain()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var template = TemplateJson(stack);

        var functionId = LogicalIdOfType(template, "AWS::Lambda::WebFunction")!;
        var revisionId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionRevision")!;
        var endpointId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionEndpoint")!;

        DependsOn(ResourceById(template, revisionId)).Should().Contain(functionId);
        DependsOn(ResourceById(template, endpointId)).Should().Contain(revisionId);
    }

    [Fact]
    public void Construct_ShouldConfigureRevision()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with
        {
            EnvironmentVariables = new Dictionary<string, string> { ["TABLE_NAME"] = "table" },
            MaxConcurrencyPerEnvironment = 50,
            Description = "test revision",
        };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var revision = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionRevision")!.Value;
        var properties = revision.GetProperty("Properties");

        properties.GetProperty("BuildConfig").GetProperty("RuntimeConfig").GetProperty("Runtime").GetString()
            .Should().Be("nodejs24.x");
        properties.GetProperty("BuildConfig").GetProperty("CodeConfig").GetProperty("S3Object")
            .GetProperty("Key").GetString().Should().Be("app.zip");
        properties.GetProperty("Description").GetString().Should().Be("test revision");

        var serviceConfig = properties.GetProperty("ServiceConfig");
        serviceConfig.GetProperty("TimeoutSeconds").GetDouble().Should().Be(30);
        serviceConfig.GetProperty("MaxConcurrencyPerEnvironment").GetDouble().Should().Be(50);
        serviceConfig.GetProperty("EnvironmentVariables").GetProperty("TABLE_NAME").GetString().Should().Be("table");

        properties.TryGetProperty("KmsKeyArn", out _).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShouldOmitVersionIdWhenNotPinned()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var revision = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").GetProperty("BuildConfig").GetProperty("CodeConfig")
            .GetProperty("S3Object").TryGetProperty("VersionId", out _).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShouldIncludeVersionIdWhenPinned()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with
        {
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions
                {
                    BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
                    ObjectVersion = "v1",
                }),
        };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var revision = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").GetProperty("BuildConfig").GetProperty("CodeConfig")
            .GetProperty("S3Object").GetProperty("VersionId").GetString().Should().Be("v1");
    }

    [Fact]
    public void Construct_ShouldExportEntryPointAsEnvironmentVariable()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with { EntryPoint = "server/index.js" };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var revision = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").GetProperty("ServiceConfig").GetProperty("EnvironmentVariables")
            .GetProperty("AWS_LAMBDA_NODEJS_ENTRYPOINT").GetString().Should().Be("server/index.js");
    }

    [Fact]
    public void Construct_ShouldCreateDefaultExecutionRole()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var role = ResourceOfType(TemplateJson(stack), "AWS::IAM::Role")!.Value;
        JsonSerializer.Serialize(role).Should().Contain("service-role/AWSLambdaBasicExecutionRole");
    }

    [Fact]
    public void Construct_ShouldUseSuppliedRoleAndNotCreateOne()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var suppliedRole = new Role(stack, "SuppliedRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
        });

        var props = DefaultProps(artifacts) with { Role = suppliedRole };
        var construct = new WebFunctionConstruct(stack, "web-function", props);

        construct.Role.Should().BeSameAs(suppliedRole);

        var template = TemplateJson(stack);
        var role = ResourceOfType(template, "AWS::IAM::Role")!.Value;
        role.TryGetProperty("Properties", out var roleProperties).Should().BeTrue();
        roleProperties.TryGetProperty("ManagedPolicyArns", out _).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShouldCreateDefaultLogGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var construct = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        construct.LogGroup.Should().NotBeNull();

        var logGroup = ResourceOfType(TemplateJson(stack), "AWS::Logs::LogGroup")!.Value;
        var properties = logGroup.GetProperty("Properties");
        properties.GetProperty("LogGroupName").GetString().Should().Be("/aws/lambda/web/test-web-function");
        properties.GetProperty("RetentionInDays").GetInt32().Should().Be(14);
        logGroup.GetProperty("DeletionPolicy").GetString().Should().Be("Delete");
    }

    [Fact]
    public void Construct_ShouldCreateLogGroupWithCustomRetention()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with { Logs = WebFunctionLogs.Create(RetentionDays.ONE_MONTH) };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var logGroup = ResourceOfType(TemplateJson(stack), "AWS::Logs::LogGroup")!.Value;
        logGroup.GetProperty("Properties").GetProperty("RetentionInDays").GetInt32().Should().Be(30);
    }

    [Fact]
    public void Construct_ShouldNotCreateLogGroupForServiceDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with { Logs = WebFunctionLogs.ServiceDefault() };
        var construct = new WebFunctionConstruct(stack, "web-function", props);

        construct.LogGroup.Should().BeNull();

        var template = TemplateJson(stack);
        ResourceOfType(template, "AWS::Logs::LogGroup").Should().BeNull();

        var revision = ResourceOfType(template, "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").TryGetProperty("TelemetryConfig", out _).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShouldUseSuppliedLogGroupWithoutCreatingOneAndGrantWrite()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var logGroup = new LogGroup(stack, "ExternalLogGroup", new LogGroupProps
        {
            LogGroupName = "/custom/logs",
        });

        var props = DefaultProps(artifacts) with { Logs = WebFunctionLogs.Use(logGroup) };
        var construct = new WebFunctionConstruct(stack, "web-function", props);

        construct.LogGroup.Should().BeSameAs(logGroup);

        var template = TemplateJson(stack);
        var logGroupId = LogicalIdOfType(template, "AWS::Logs::LogGroup")!;
        var revision = ResourceOfType(template, "AWS::Lambda::WebFunctionRevision")!.Value;
        revision.GetProperty("Properties").GetProperty("ServiceConfig").GetProperty("TelemetryConfig")
            .GetProperty("LoggingConfig").GetProperty("LogGroup").GetProperty("Ref").GetString()
            .Should().Be(logGroupId);

        JsonSerializer.Serialize(template).Should().Contain("logs:PutLogEvents");
    }

    [Fact]
    public void Construct_ShouldNotGrantWriteForAwsLambdaLogGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var logGroup = new LogGroup(stack, "ExternalLogGroup", new LogGroupProps
        {
            LogGroupName = "/aws/lambda/web/external",
        });

        var props = DefaultProps(artifacts) with { Logs = WebFunctionLogs.Use(logGroup) };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var role = ResourceOfType(TemplateJson(stack), "AWS::IAM::Role")!.Value;
        JsonSerializer.Serialize(role).Should().NotContain("logs:PutLogEvents");
    }

    [Fact]
    public void Construct_ShouldConfigureEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with
        {
            Endpoint = new WebFunctionEndpointOptions
            {
                EndpointName = "public",
                AuthType = WebFunctionAuthType.ApplicationManaged,
                EndpointType = WebFunctionEndpointType.MultiRegion,
                Regions = ["us-west-2"],
                MaxEnvironments = 12,
                RateLimit = 600,
                Description = "public endpoint",
            },
        };

        _ = new WebFunctionConstruct(stack, "web-function", props);

        var endpoint = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionEndpoint")!.Value;
        var properties = endpoint.GetProperty("Properties");

        properties.GetProperty("AuthType").GetString().Should().Be("ApplicationManaged");
        properties.GetProperty("EndpointName").GetString().Should().Be("public");
        properties.GetProperty("EndpointType").GetString().Should().Be("MultiRegion");
        properties.GetProperty("Regions")[0].GetString().Should().Be("us-west-2");
        properties.GetProperty("Description").GetString().Should().Be("public endpoint");
        properties.GetProperty("ScalingConfig").GetProperty("MaxEnvironments").GetDouble().Should().Be(12);
        properties.GetProperty("ThrottleConfig").GetProperty("RateLimit").GetDouble().Should().Be(600);
    }

    [Fact]
    public void Construct_ShouldOmitScalingAndThrottleWhenUnset()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var endpoint = ResourceOfType(TemplateJson(stack), "AWS::Lambda::WebFunctionEndpoint")!.Value;
        var properties = endpoint.GetProperty("Properties");

        properties.TryGetProperty("ScalingConfig", out _).Should().BeFalse();
        properties.TryGetProperty("ThrottleConfig", out _).Should().BeFalse();
        properties.TryGetProperty("Regions", out _).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShouldSetRevisionWeightTo100()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        _ = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        var template = TemplateJson(stack);
        var revisionId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionRevision")!;
        var endpoint = ResourceOfType(template, "AWS::Lambda::WebFunctionEndpoint")!.Value;

        var weight = endpoint.GetProperty("Properties").GetProperty("RevisionWeights")[0];
        weight.GetProperty("Weight").GetDouble().Should().Be(100);
        weight.GetProperty("RevisionId").GetProperty("Fn::GetAtt")[0].GetString().Should().Be(revisionId);
    }

    [Fact]
    public void Construct_ShouldExposeGrantPrincipalAsRole()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var construct = new WebFunctionConstruct(stack, "web-function", DefaultProps(artifacts));

        construct.GrantPrincipal.Should().BeSameAs(construct.Role.GrantPrincipal);

        var table = new Table(stack, "Table", new TableProps
        {
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING },
        });

        var act = () => table.GrantReadData(construct);

        act.Should().NotThrow();

        JsonSerializer.Serialize(TemplateJson(stack)).Should().Contain("dynamodb:GetItem");
    }

    [Fact]
    public void Construct_ShouldAddBucketPolicyForOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });

        _ = new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.Automatic));

        var template = TemplateJson(stack);
        var policy = ResourceOfType(template, "AWS::S3::BucketPolicy")!.Value;
        var document = JsonSerializer.Serialize(policy.GetProperty("Properties").GetProperty("PolicyDocument"));

        document.Should().Contain("s3:GetObject");
        document.Should().Contain("s3:GetObjectVersion");
        document.Should().Contain("aws:SourceAccount");
        document.Should().Contain("web-function/test-web-function/revision/*");

        var revisionId = LogicalIdOfType(template, "AWS::Lambda::WebFunctionRevision")!;
        var bucketPolicyId = LogicalIdOfType(template, "AWS::S3::BucketPolicy")!;
        DependsOn(ResourceById(template, revisionId)).Should().Contain(bucketPolicyId);
    }

    [Fact]
    public void Construct_ShouldThrowForAutomaticImportedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.Automatic));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Construct_ShouldNotAddPolicyForExternallyManagedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });

        _ = new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        ResourceOfType(TemplateJson(stack), "AWS::S3::BucketPolicy").Should().BeNull();
    }

    [Fact]
    public void Construct_ShouldThrowForUnversionedOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = false });
        var props = DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged);

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Construct_ShouldThrowForKmsManagedOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps
        {
            Versioned = true,
            Encryption = BucketEncryption.KMS_MANAGED,
        });
        var props = DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged);

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Construct_ShouldThrowForCrossStackOwnedBucketWithUnresolvedFunctionEnv()
    {
        var app = new App();
        var functionStack = new Stack(app, "FunctionStack");
        var bucketStack = new Stack(app, "BucketStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });

        var artifacts = new Bucket(bucketStack, "Artifacts", new BucketProps { Versioned = true });
        var props = DefaultProps(artifacts, WebFunctionCodeBucketPolicy.Automatic);

        var act = () => new WebFunctionConstruct(functionStack, "web-function", props);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Construct_ShouldAcceptEnabledOwnedBucketWithS3ManagedEncryption()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps
        {
            Versioned = true,
            Encryption = BucketEncryption.S3_MANAGED,
        });

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.Automatic));

        act.Should().NotThrow();
        ResourceOfType(TemplateJson(stack), "AWS::S3::BucketPolicy").Should().NotBeNull();
    }

    [Fact]
    public void Construct_ShouldThrowForDisabledOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = false });

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().Throw<InvalidOperationException>().WithMessage("*versioning*");
    }

    [Fact]
    public void Construct_ShouldThrowForSuspendedOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketVersioningStatus(artifacts, "Suspended");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().Throw<InvalidOperationException>().WithMessage("*versioning*");
    }

    [Fact]
    public void Construct_ShouldSkipTokenizedVersioningStatus()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketVersioningStatus(artifacts, Fn.Ref("VersioningStatus"));

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.Automatic));

        act.Should().NotThrow();
        ResourceOfType(TemplateJson(stack), "AWS::S3::BucketPolicy").Should().NotBeNull();
    }

    [Fact]
    public void Construct_ShouldSkipUnrecognizedVersioningStatus()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketVersioningStatus(artifacts, "SomeFutureState");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldThrowForAwsManagedKmsOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(artifacts, "aws:kms", "aws/s3");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().Throw<InvalidOperationException>().WithMessage("*aws/s3*");
    }

    [Fact]
    public void Construct_ShouldAcceptCustomerManagedKmsOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(
            artifacts,
            "aws:kms",
            "arn:aws:kms:us-east-1:123456789012:key/11111111-1111-1111-1111-111111111111");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldSkipTokenizedKmsKeyOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(artifacts, "aws:kms", Fn.Ref("KmsKeyId"));

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldAcceptDssEWithCustomerManagedKeyOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(
            artifacts,
            "aws:kms:dsse",
            "arn:aws:kms:us-east-1:123456789012:key/11111111-1111-1111-1111-111111111111");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldThrowForDssEManagedOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(artifacts, "aws:kms:dsse", null);

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().Throw<InvalidOperationException>().WithMessage("*DSSE*customer-managed*");
    }

    [Fact]
    public void Construct_ShouldThrowForDssEWithAwsManagedKeyOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        SetOwnedBucketEncryption(artifacts, "aws:kms:dsse", "aws/s3");

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().Throw<InvalidOperationException>().WithMessage("*customer-managed*");
    }

    [Fact]
    public void Construct_ShouldSkipUnrecognizedEncryptionStructureOwnedBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = new Bucket(stack, "Artifacts", new BucketProps { Versioned = true });
        CfnBucketOf(artifacts).BucketEncryption = new CfnBucket.BucketEncryptionProperty
        {
            ServerSideEncryptionConfiguration = new object[]
            {
                new CfnBucket.ServerSideEncryptionRuleProperty(),
            },
        };

        var act = () => new WebFunctionConstruct(
            stack,
            "web-function",
            DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged));

        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldNotRequireResolvedFunctionEnvForExternallyManagedCrossStackBucket()
    {
        var app = new App();
        var functionStack = new Stack(app, "FunctionStack");
        var bucketStack = new Stack(app, "BucketStack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });

        var artifacts = new Bucket(bucketStack, "Artifacts", new BucketProps { Versioned = true });
        var props = DefaultProps(artifacts, WebFunctionCodeBucketPolicy.ExternallyManaged);

        var act = () => new WebFunctionConstruct(functionStack, "web-function", props);

        // ExternallyManaged renders no bucket policy, so the Automatic-only cross-stack account/Region guard
        // must not apply. (The function stack itself cannot be synthesized: the cross-environment bucket
        // reference is a CDK-level restriction independent of this validation.)
        act.Should().NotThrow();
    }

    [Fact]
    public void Construct_ShouldAllowTokenFunctionName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");

        var props = DefaultProps(artifacts) with { FunctionName = Fn.Ref("SomeName") };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("has/slash")]
    public void Construct_ShouldThrowForInvalidFunctionName(string functionName)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with { FunctionName = functionName };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(901)]
    public void Construct_ShouldThrowForOutOfRangeTimeout(int seconds)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with { Timeout = Duration.Seconds(seconds) };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public void Construct_ShouldThrowForOutOfRangeConcurrency(int concurrency)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with { MaxConcurrencyPerEnvironment = concurrency };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_ShouldThrowForInvalidEnvironmentVariableKey()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            EnvironmentVariables = new Dictionary<string, string> { ["1INVALID"] = "value" },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_ShouldThrowForReservedEnvironmentVariable()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            EnvironmentVariables = new Dictionary<string, string> { ["AWS_LAMBDA_HTTP_ENDPOINT"] = "value" },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_ShouldThrowForEntryPointConflict()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            EntryPoint = "index.js",
            EnvironmentVariables = new Dictionary<string, string> { ["AWS_LAMBDA_NODEJS_ENTRYPOINT"] = "other.js" },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("/absolute.js")]
    [InlineData("../escape.js")]
    public void Construct_ShouldThrowForInvalidEntryPoint(string entryPoint)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with { EntryPoint = entryPoint };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public void Construct_ShouldThrowForInvalidEndpointName(string endpointName)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            Endpoint = new WebFunctionEndpointOptions
            {
                EndpointName = endpointName,
                AuthType = WebFunctionAuthType.IamAuth,
            },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10001)]
    public void Construct_ShouldThrowForOutOfRangeMaxEnvironments(int maxEnvironments)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
                MaxEnvironments = maxEnvironments,
            },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(150)]
    [InlineData(1500)]
    public void Construct_ShouldThrowForOutOfRangeRateLimit(int rateLimit)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
                RateLimit = rateLimit,
            },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_ShouldThrowForMultiRegionEndpointWithoutNonHomeRegion()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = DefaultProps(artifacts) with
        {
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
                EndpointType = WebFunctionEndpointType.MultiRegion,
                Regions = ["us-east-1"],
            },
        };

        var act = () => new WebFunctionConstruct(stack, "web-function", props);

        act.Should().Throw<ArgumentException>();
    }
}

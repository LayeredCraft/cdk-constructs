using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Route53;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Golden;
using DynamoAttribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Golden-template compatibility tests for the constructs that already shipped before the Lambda Web
/// Functions work (design §12.4). These pin the complete synthesized CloudFormation template so an
/// <c>Amazon.CDK.Lib</c> bump cannot silently change a deployed construct.
/// </summary>
[Collection("CDK Tests")]
public class ExistingConstructsGoldenTemplateTests
{
    [Theory]
    [InlineData("lambda-default.json", false, false, false)]
    [InlineData("lambda-otel-amd64.json", true, false, false)]
    [InlineData("lambda-otel-arm64.json", true, true, false)]
    [InlineData("lambda-permissions.json", false, false, true)]
    [InlineData("lambda-url.json", false, false, false, true)]
    [InlineData("lambda-otel-url-permissions.json", true, false, true, true)]
    public void LambdaFunctionConstruct_ShouldMatchGoldenTemplate(
        string goldenFileName,
        bool includeOtelLayer,
        bool useArm64,
        bool includePermissions,
        bool generateUrl = false)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-lambda");

        var props = new LambdaFunctionConstructProps
        {
            FunctionName = "golden-function",
            FunctionSuffix = "test",
            AssetPath = CdkTestHelper.GetTestLambdaZipPath(),
            RoleName = "golden-role",
            PolicyName = "golden-policy",
            MemorySize = 1024,
            TimeoutInSeconds = 6,
            IncludeOtelLayer = includeOtelLayer,
            Architecture = useArm64 ? "arm64" : "amd64",
            GenerateUrl = generateUrl,
            EnvironmentVariables = new Dictionary<string, string> { ["TABLE_NAME"] = "golden-table" },
            Permissions = includePermissions
                ?
                [
                    new LambdaPermission
                    {
                        Principal = "apigateway.amazonaws.com",
                        Action = "lambda:InvokeFunction",
                        SourceArn = "arn:aws:execute-api:us-east-1:123456789012:api-id/*/GET/golden"
                    }
                ]
                : []
        };

        _ = new LambdaFunctionConstruct(stack, "golden-lambda", props);

        GoldenTemplate.AssertMatches(stack, goldenFileName);
    }

    [Theory]
    [InlineData("static-site-default.json", false, false)]
    [InlineData("static-site-api.json", true, false)]
    [InlineData("static-site-alternates.json", false, true)]
    [InlineData("static-site-full.json", true, true)]
    public void StaticSiteConstruct_ShouldMatchGoldenTemplate(
        string goldenFileName,
        bool includeApiDomain,
        bool includeAlternateDomains)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-static-site");

        var props = new StaticSiteConstructProps
        {
            DomainName = "example.com",
            SiteSubDomain = "www",
            AssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site"),
            ApiDomain = includeApiDomain ? "api.example.com" : null,
            AlternateDomains = includeAlternateDomains ? ["alt.example.com"] : []
        };

        _ = new StaticSiteConstruct(stack, "golden-static-site", props);

        GoldenTemplate.AssertMatches(stack, goldenFileName);
    }

    [Fact]
    public void DynamoDbTableConstruct_PartitionKeyOnly_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-dynamodb");

        _ = new DynamoDbTableConstruct(stack, "golden-table", new DynamoDbTableConstructProps
        {
            TableName = "golden-table",
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING }
        });

        GoldenTemplate.AssertMatches(stack, "dynamodb-default.json");
    }

    [Fact]
    public void DynamoDbTableConstruct_SortKey_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-dynamodb");

        _ = new DynamoDbTableConstruct(stack, "golden-table", new DynamoDbTableConstructProps
        {
            TableName = "golden-table",
            RemovalPolicy = RemovalPolicy.RETAIN,
            BillingMode = BillingMode.PROVISIONED,
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "sk", Type = AttributeType.NUMBER }
        });

        GoldenTemplate.AssertMatches(stack, "dynamodb-sort-key.json");
    }

    [Fact]
    public void DynamoDbTableConstruct_GlobalSecondaryIndexes_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-dynamodb");

        _ = new DynamoDbTableConstruct(stack, "golden-table", new DynamoDbTableConstructProps
        {
            TableName = "golden-table",
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING },
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndexProps
                {
                    IndexName = "gsi-email",
                    PartitionKey = new DynamoAttribute { Name = "email", Type = AttributeType.STRING }
                }
            ]
        });

        GoldenTemplate.AssertMatches(stack, "dynamodb-gsi.json");
    }

    [Fact]
    public void DynamoDbTableConstruct_StreamAndTtl_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-dynamodb");

        _ = new DynamoDbTableConstruct(stack, "golden-table", new DynamoDbTableConstructProps
        {
            TableName = "golden-table",
            RemovalPolicy = RemovalPolicy.DESTROY,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PartitionKey = new DynamoAttribute { Name = "pk", Type = AttributeType.STRING },
            Stream = StreamViewType.NEW_AND_OLD_IMAGES,
            TimeToLiveAttribute = "expiresAt"
        });

        GoldenTemplate.AssertMatches(stack, "dynamodb-stream-ttl.json");
    }

    [Fact]
    public void CognitoUserPoolConstruct_PrefixDomain_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-cognito");

        _ = new CognitoUserPoolConstruct(stack, "golden-user-pool", new CognitoUserPoolConstructProps
        {
            UserPoolName = "golden-user-pool",
            Domain = new CognitoUserPoolDomainProps { CognitoDomainPrefix = "golden-pool" },
            ResourceServers =
            [
                new CognitoResourceServerProps
                {
                    Name = "GoldenApi",
                    Identifier = "https://api.golden.example.com",
                    Scopes =
                    [
                        new CognitoResourceServerScopeProps { Name = "read", Description = "Read access" }
                    ]
                }
            ],
            AppClients =
            [
                new CognitoUserPoolAppClientProps
                {
                    Name = "Golden Web Client",
                    CallbackUrls = ["https://app.example.com/callback"],
                    LogoutUrls = ["https://app.example.com/logout"]
                }
            ],
            Groups =
            [
                new CognitoUserPoolGroupProps("Admin", "Administrators", 1, null)
            ]
        });

        GoldenTemplate.AssertMatches(stack, "cognito-prefix-domain.json");
    }

    [Fact]
    public void CognitoUserPoolConstruct_CustomDomain_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-cognito");

        var zone = HostedZone.FromHostedZoneAttributes(stack, "HostedZone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com"
        });

        var certificate = Certificate.FromCertificateArn(
            stack,
            "Certificate",
            "arn:aws:acm:us-east-1:123456789012:certificate/11111111-2222-3333-4444-555555555555");

        _ = new CognitoUserPoolConstruct(stack, "golden-user-pool", new CognitoUserPoolConstructProps
        {
            UserPoolName = "golden-user-pool",
            Domain = new CognitoUserPoolDomainProps
            {
                DomainName = "example.com",
                AuthSubDomain = "auth",
                Certificate = certificate,
                HostedZone = zone
            }
        });

        GoldenTemplate.AssertMatches(stack, "cognito-custom-domain.json");
    }

    [Fact]
    public void CognitoUserPoolConstruct_NoDomain_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-cognito");

        _ = new CognitoUserPoolConstruct(stack, "golden-user-pool", new CognitoUserPoolConstructProps
        {
            UserPoolName = "golden-user-pool",
            SelfSignUpEnabled = false,
            PasswordMinLength = 14,
            Mfa = Mfa.OPTIONAL,
            MfaSecondFactor = new MfaSecondFactor { Otp = true },
            Groups =
            [
                new CognitoUserPoolGroupProps("Readers", null, null, null)
            ]
        });

        GoldenTemplate.AssertMatches(stack, "cognito-no-domain.json");
    }
}

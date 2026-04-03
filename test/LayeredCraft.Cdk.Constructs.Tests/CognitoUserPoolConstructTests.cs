using Amazon.CDK;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class CognitoUserPoolConstructTests
{
    // -------------------------------------------------------------------------
    // User pool
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldCreateUserPool(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveUserPool(props.UserPoolName);
    }

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExposeUserPoolProperty(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        var construct = new CognitoUserPoolConstruct(stack, "test-pool", props);

        construct.UserPool.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Domain
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldCreateUserPoolDomain(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveCognitoUserPoolDomain(props.Domain!.CognitoDomainPrefix!);
    }

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExposeDomainProperty(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        var construct = new CognitoUserPoolConstruct(stack, "test-pool", props);

        construct.Domain.Should().NotBeNull();
    }

    [Fact]
    public void Construct_ShouldNotCreateDomain_WhenDomainIsNull()
    {
        var stack = CreateStack();
        var props = CdkTestHelper.CreateCognitoUserPoolPropsBuilder()
            .AddWebAppClient("client", ["https://example.com/callback"], ["https://example.com"])
            .WithoutDomain()
            .Build();

        var construct = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        construct.Domain.Should().BeNull();
        template.ResourceCountIs("AWS::Cognito::UserPoolDomain", 0);
    }

    // -------------------------------------------------------------------------
    // App client
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldCreateAppClient(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveUserPoolClient(props.AppClients[0].Name);
    }

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExposeAppClientsProperty(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        var construct = new CognitoUserPoolConstruct(stack, "test-pool", props);

        construct.AppClients.Should().ContainKey(props.AppClients[0].Name);
    }

    // -------------------------------------------------------------------------
    // Resource server
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldCreateResourceServer(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveResourceServer(props.ResourceServers[0].Identifier);
    }

    // -------------------------------------------------------------------------
    // Groups
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldCreateUserGroup(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveUserPoolGroup(props.Groups!.First().Name);
    }

    // -------------------------------------------------------------------------
    // Managed login branding — absent
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData(includeBranding: false)]
    public void Construct_ShouldNotCreateManagedLoginBranding_WhenBrandingIsNull(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldNotHaveManagedLoginBranding();
    }

    // -------------------------------------------------------------------------
    // Managed login branding — present
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData(includeBranding: true)]
    public void Construct_ShouldCreateManagedLoginBranding_WhenBrandingIsConfigured(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldHaveManagedLoginBranding();
    }

    [Theory]
    [CognitoUserPoolConstructAutoData(includeBranding: true)]
    public void Construct_ShouldNotCreateBranding_WhenNoBrandingOnClient(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();
        var noBrandingProps = props with
        {
            AppClients =
            [
                new CognitoUserPoolAppClientProps
                {
                    Name = props.AppClients[0].Name,
                    CallbackUrls = props.AppClients[0].CallbackUrls,
                    LogoutUrls = props.AppClients[0].LogoutUrls,
                    AllowedOAuthScopes = props.AppClients[0].AllowedOAuthScopes,
                    SupportedIdentityProviders = props.AppClients[0].SupportedIdentityProviders,
                    ManagedLoginBranding = null,
                },
            ],
        };

        _ = new CognitoUserPoolConstruct(stack, "test-pool", noBrandingProps);
        var template = Template.FromStack(stack);

        template.ShouldNotHaveManagedLoginBranding();
    }

    [Theory]
    [CognitoUserPoolConstructAutoData(includeBranding: true)]
    public void Construct_ShouldThrow_WhenBrandingSettingsJsonIsEmpty(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();
        var badProps = props with
        {
            AppClients =
            [
                new CognitoUserPoolAppClientProps
                {
                    Name = props.AppClients[0].Name,
                    CallbackUrls = props.AppClients[0].CallbackUrls,
                    LogoutUrls = props.AppClients[0].LogoutUrls,
                    AllowedOAuthScopes = props.AppClients[0].AllowedOAuthScopes,
                    SupportedIdentityProviders = props.AppClients[0].SupportedIdentityProviders,
                    ManagedLoginBranding = new CognitoManagedLoginBrandingProps(SettingsJson: ""),
                },
            ],
        };

        var act = () => new CognitoUserPoolConstruct(stack, "test-pool", badProps);

        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // CloudFormation outputs
    // -------------------------------------------------------------------------

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExportUserPoolId(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldExportUserPoolId("test-stack", "test-pool");
    }

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExportUserPoolArn(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldExportUserPoolArn("test-stack", "test-pool");
    }

    [Theory]
    [CognitoUserPoolConstructAutoData]
    public void Construct_ShouldExportAppClientId(CognitoUserPoolConstructProps props)
    {
        var stack = CreateStack();

        _ = new CognitoUserPoolConstruct(stack, "test-pool", props);
        var template = Template.FromStack(stack);

        template.ShouldExportAppClientId("test-stack", "test-pool", props.AppClients[0].Name);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Stack CreateStack() => new(new App(), "test-stack");
}

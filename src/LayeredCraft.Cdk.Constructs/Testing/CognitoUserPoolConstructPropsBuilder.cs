using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for creating <see cref="CognitoUserPoolConstructProps"/> instances in tests.
/// Provides sensible defaults and allows targeted customization of specific properties.
/// </summary>
public class CognitoUserPoolConstructPropsBuilder
{
    private string _userPoolName = "test-user-pool";
    private bool _selfSignUpEnabled = true;
    private RemovalPolicy _removalPolicy = RemovalPolicy.DESTROY;
    private Mfa _mfa = Mfa.OFF;
    private int _passwordMinLength = 12;
    private ICognitoUserPoolDomainProps? _domain = new CognitoUserPoolDomainProps
    {
        CognitoDomainPrefix = "test-user-pool",
        ManagedLoginVersion = CognitoManagedLoginVersion.ManagedLogin,
        CreateRoute53Record = false,
    };
    private readonly List<ICognitoResourceServerProps> _resourceServers = [];
    private readonly List<ICognitoUserPoolAppClientProps> _appClients = [];
    private readonly List<ICognitoUserPoolGroupProps> _groups = [];

    /// <summary>Sets the Cognito user pool name.</summary>
    public CognitoUserPoolConstructPropsBuilder WithUserPoolName(string userPoolName)
    {
        _userPoolName = userPoolName;
        return this;
    }

    /// <summary>Enables or disables self sign-up. Defaults to <see langword="true"/>.</summary>
    public CognitoUserPoolConstructPropsBuilder WithSelfSignUpEnabled(bool enabled)
    {
        _selfSignUpEnabled = enabled;
        return this;
    }

    /// <summary>Sets the removal policy. Defaults to <see cref="RemovalPolicy.DESTROY"/>.</summary>
    public CognitoUserPoolConstructPropsBuilder WithRemovalPolicy(RemovalPolicy removalPolicy)
    {
        _removalPolicy = removalPolicy;
        return this;
    }

    /// <summary>Sets the MFA requirement. Defaults to <see cref="Mfa.OFF"/>.</summary>
    public CognitoUserPoolConstructPropsBuilder WithMfa(Mfa mfa)
    {
        _mfa = mfa;
        return this;
    }

    /// <summary>Sets the minimum password length. Defaults to <c>12</c>.</summary>
    public CognitoUserPoolConstructPropsBuilder WithPasswordMinLength(int minLength)
    {
        _passwordMinLength = minLength;
        return this;
    }

    /// <summary>
    /// Configures a Cognito-hosted domain with the given prefix and Managed Login v2.
    /// Use this in tests to avoid requiring AWS environment context (account/region) for custom domains.
    /// </summary>
    public CognitoUserPoolConstructPropsBuilder WithCognitoDomain(
        string domainPrefix,
        CognitoManagedLoginVersion version = CognitoManagedLoginVersion.ManagedLogin)
    {
        _domain = new CognitoUserPoolDomainProps
        {
            CognitoDomainPrefix = domainPrefix,
            ManagedLoginVersion = version,
            CreateRoute53Record = false,
        };
        return this;
    }

    /// <summary>Sets a fully custom domain props object.</summary>
    public CognitoUserPoolConstructPropsBuilder WithDomain(ICognitoUserPoolDomainProps domain)
    {
        _domain = domain;
        return this;
    }

    /// <summary>Removes the domain configuration so no <c>AWS::Cognito::UserPoolDomain</c> is created.</summary>
    public CognitoUserPoolConstructPropsBuilder WithoutDomain()
    {
        _domain = null;
        return this;
    }

    /// <summary>Adds a resource server with the given scopes.</summary>
    public CognitoUserPoolConstructPropsBuilder AddResourceServer(
        string name,
        string identifier,
        IReadOnlyList<ICognitoResourceServerScopeProps>? scopes = null)
    {
        _resourceServers.Add(new CognitoResourceServerProps
        {
            Name = name,
            Identifier = identifier,
            Scopes = scopes ?? [],
        });
        return this;
    }

    /// <summary>Adds an app client using a pre-built props object.</summary>
    public CognitoUserPoolConstructPropsBuilder AddAppClient(ICognitoUserPoolAppClientProps client)
    {
        _appClients.Add(client);
        return this;
    }

    /// <summary>
    /// Adds a standard web app client with Authorization Code Grant and optional Managed Login branding.
    /// </summary>
    public CognitoUserPoolConstructPropsBuilder AddWebAppClient(
        string name,
        IReadOnlyList<string> callbackUrls,
        IReadOnlyList<string> logoutUrls,
        ICognitoManagedLoginBrandingProps? branding = null)
    {
        _appClients.Add(new CognitoUserPoolAppClientProps
        {
            Name = name,
            GenerateSecret = false,
            CallbackUrls = callbackUrls,
            LogoutUrls = logoutUrls,
            AuthorizationCodeGrant = true,
            ImplicitCodeGrant = false,
            ClientCredentials = false,
            AllowedOAuthScopes = [OAuthScope.OPENID, OAuthScope.EMAIL],
            SupportedIdentityProviders = [UserPoolClientIdentityProvider.COGNITO],
            ManagedLoginBranding = branding,
        });
        return this;
    }

    /// <summary>Adds a user group.</summary>
    public CognitoUserPoolConstructPropsBuilder AddGroup(
        string name,
        string? description = null,
        int? precedence = null)
    {
        _groups.Add(new CognitoUserPoolGroupProps(name, description, precedence));
        return this;
    }

    /// <summary>
    /// Configures a complete web application setup: named user pool, Cognito-hosted domain,
    /// a single web client, and a default resource server.
    /// Provides a convenient starting point that can be further customised with additional calls.
    /// </summary>
    public CognitoUserPoolConstructPropsBuilder ForWebApplication(
        string userPoolName = "test-user-pool",
        string cognitoDomainPrefix = "test-user-pool")
    {
        return WithUserPoolName(userPoolName)
            .WithCognitoDomain(cognitoDomainPrefix)
            .AddWebAppClient(
                name: "web-client",
                callbackUrls: ["https://example.com/callback"],
                logoutUrls: ["https://example.com"]);
    }

    /// <summary>Builds the <see cref="CognitoUserPoolConstructProps"/> instance.</summary>
    public CognitoUserPoolConstructProps Build()
    {
        return new CognitoUserPoolConstructProps
        {
            UserPoolName = _userPoolName,
            SelfSignUpEnabled = _selfSignUpEnabled,
            RemovalPolicy = _removalPolicy,
            Mfa = _mfa,
            PasswordMinLength = _passwordMinLength,
            Domain = _domain,
            ResourceServers = [.. _resourceServers],
            AppClients = [.. _appClients],
            Groups = [.. _groups],
        };
    }
}

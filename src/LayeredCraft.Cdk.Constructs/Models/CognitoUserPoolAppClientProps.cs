using Amazon.CDK.AWS.Cognito;

namespace LayeredCraft.Cdk.Constructs.Models;

public interface ICognitoUserPoolAppClientProps
{
    string Name { get; }

    bool GenerateSecret { get; }

    IReadOnlyList<string> CallbackUrls { get; }

    IReadOnlyList<string> LogoutUrls { get; }

    bool AuthorizationCodeGrant { get; }

    bool ImplicitCodeGrant { get; }

    bool ClientCredentials { get; }

    IReadOnlyList<OAuthScope> AllowedOAuthScopes { get; }

    IReadOnlyList<UserPoolClientIdentityProvider> SupportedIdentityProviders { get; }

    /// <summary>
    /// Optional Managed Login branding configuration to associate with this app client.
    /// When <see langword="null"/>, no <c>AWS::Cognito::ManagedLoginBranding</c> resource is created
    /// for the client and Cognito's default styling is used.
    /// </summary>
    ICognitoManagedLoginBrandingProps? ManagedLoginBranding { get; }
}

public sealed record CognitoUserPoolAppClientProps : ICognitoUserPoolAppClientProps
{
    public required string Name { get; init; }

    public bool GenerateSecret { get; init; } = false;

    public IReadOnlyList<string> CallbackUrls { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> LogoutUrls { get; init; } = Array.Empty<string>();

    public bool AuthorizationCodeGrant { get; init; } = true;

    public bool ImplicitCodeGrant { get; init; } = false;

    public bool ClientCredentials { get; init; } = false;

    public IReadOnlyList<OAuthScope> AllowedOAuthScopes { get; init; } =
        [OAuthScope.OPENID, OAuthScope.EMAIL];

    public IReadOnlyList<UserPoolClientIdentityProvider> SupportedIdentityProviders { get; init; } =
        [UserPoolClientIdentityProvider.COGNITO];

    /// <inheritdoc />
    public ICognitoManagedLoginBrandingProps? ManagedLoginBranding { get; init; }
}
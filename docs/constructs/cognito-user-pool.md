# Cognito User Pool Construct

The `CognitoUserPoolConstruct` provides a production-ready Amazon Cognito User Pool with support for custom domains, resource servers, OAuth app clients, user groups, and Managed Login branding (v2).

## Features

- **:busts_in_silhouette: User Pool**: Email-based sign-in with auto-verification, configurable self sign-up, and password policy
- **:globe_with_meridians: Domain Modes**: Cognito-hosted domain prefix or fully custom domain with ACM certificate and Route53 record
- **:shield: Resource Servers**: Define API scopes for machine-to-machine or user-delegated authorization
- **:iphone: App Clients**: Multiple OAuth 2.0 app clients with configurable flows, scopes, and identity providers
- **:busts_in_silhouette: User Groups**: Named groups with optional precedence and IAM role assignment
- **:art: Managed Login Branding**: Full Cognito Managed Login v2 branding via settings JSON and optional image assets
- **:outbox_tray: CloudFormation Outputs**: Automatic exports for user pool ID, ARN, and each app client ID

## Basic Usage

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var pool = new CognitoUserPoolConstruct(this, "my-user-pool", new CognitoUserPoolConstructProps
        {
            UserPoolName = "my-app-users",
            SelfSignUpEnabled = true,
            Domain = new CognitoUserPoolDomainProps
            {
                CognitoDomainPrefix = "my-app-auth",
            },
            AppClients =
            [
                new CognitoUserPoolAppClientProps
                {
                    Name = "my-web-app",
                    CallbackUrls = ["https://example.com/authentication/login-callback"],
                    LogoutUrls = ["https://example.com"],
                    AllowedOAuthScopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
                },
            ],
        });
    }
}
```

## Configuration Properties

### Root Properties (`CognitoUserPoolConstructProps`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UserPoolName` | `string` | **required** | Name of the Cognito user pool |
| `SelfSignUpEnabled` | `bool` | `true` | Whether users can self-register |
| `RemovalPolicy` | `RemovalPolicy` | `DESTROY` | Behavior when the stack is deleted |
| `Mfa` | `Mfa` | `OFF` | MFA requirement (`OFF`, `OPTIONAL`, `REQUIRED`) |
| `PasswordMinLength` | `int` | `12` | Minimum password length |
| `Domain` | `ICognitoUserPoolDomainProps?` | `null` | Domain configuration (Cognito prefix or custom) |
| `ResourceServers` | `IReadOnlyList<ICognitoResourceServerProps>` | `[]` | OAuth resource server definitions |
| `AppClients` | `IReadOnlyList<ICognitoUserPoolAppClientProps>` | `[]` | OAuth app client definitions |
| `Groups` | `IReadOnlyCollection<ICognitoUserPoolGroupProps>?` | `[]` | User group definitions |

### Domain Properties (`CognitoUserPoolDomainProps`)

Exactly one of `CognitoDomainPrefix` or (`DomainName` + `AuthSubDomain`) must be set.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CognitoDomainPrefix` | `string?` | `null` | Cognito-hosted domain prefix (`{prefix}.auth.us-east-1.amazoncognito.com`) |
| `DomainName` | `string?` | `null` | Root domain for custom domain (e.g., `example.com`) |
| `AuthSubDomain` | `string?` | `null` | Subdomain for custom domain (e.g., `auth` → `auth.example.com`) |
| `ManagedLoginVersion` | `CognitoManagedLoginVersion` | `ManagedLogin` | `ClassicHostedUi` or `ManagedLogin` (v2) |
| `CreateRoute53Record` | `bool` | `true` | Whether to create an A-alias record in the hosted zone (custom domain only) |

### App Client Properties (`CognitoUserPoolAppClientProps`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string` | **required** | App client name |
| `GenerateSecret` | `bool` | `false` | Whether to generate a client secret |
| `CallbackUrls` | `IReadOnlyList<string>` | `[]` | Allowed redirect URIs after login |
| `LogoutUrls` | `IReadOnlyList<string>` | `[]` | Allowed redirect URIs after logout |
| `AuthorizationCodeGrant` | `bool` | `true` | Enable Authorization Code grant |
| `ImplicitCodeGrant` | `bool` | `false` | Enable Implicit grant |
| `ClientCredentials` | `bool` | `false` | Enable Client Credentials grant |
| `AllowedOAuthScopes` | `IReadOnlyList<OAuthScope>` | `[OPENID, EMAIL]` | Permitted OAuth scopes |
| `SupportedIdentityProviders` | `IReadOnlyList<UserPoolClientIdentityProvider>` | `[COGNITO]` | Identity providers |
| `ManagedLoginBranding` | `ICognitoManagedLoginBrandingProps?` | `null` | Optional Managed Login branding |

### Resource Server Properties (`CognitoResourceServerProps`)

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Display name for the resource server |
| `Identifier` | `string` | Unique URI identifier (e.g., `my-api`) |
| `Scopes` | `IReadOnlyList<ICognitoResourceServerScopeProps>` | Scope definitions |

Each scope (`CognitoResourceServerScopeProps`) has:
- `Name` — scope name (e.g., `read`)
- `Description` — human-readable description

Use the scope as `OAuthScope.Custom("my-api/read")` in app client `AllowedOAuthScopes`.

### User Group Properties (`CognitoUserPoolGroupProps`)

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Group name |
| `Description` | `string?` | Optional group description |
| `Precedence` | `int?` | Precedence for group priority (lower = higher priority) |
| `RoleArn` | `string?` | Optional IAM role ARN to associate with the group |

### Managed Login Branding Properties (`CognitoManagedLoginBrandingProps`)

| Property | Type | Description |
|----------|------|-------------|
| `SettingsJson` | `string` | Full Managed Login settings JSON (see Cognito docs for schema) |
| `Assets` | `IReadOnlyList<ICognitoManagedLoginAssetProps>?` | Optional image assets (logo, favicon, etc.) |

## Advanced Examples

### Custom Domain with Route53

```csharp
var pool = new CognitoUserPoolConstruct(this, "auth-pool", new CognitoUserPoolConstructProps
{
    UserPoolName = "my-app-users",
    Domain = new CognitoUserPoolDomainProps
    {
        DomainName = "example.com",       // Hosted zone must exist in this account
        AuthSubDomain = "auth",           // → auth.example.com
        ManagedLoginVersion = CognitoManagedLoginVersion.ManagedLogin,
        CreateRoute53Record = true,
    },
    AppClients =
    [
        new CognitoUserPoolAppClientProps
        {
            Name = "web-app",
            CallbackUrls = ["https://example.com/authentication/login-callback"],
            LogoutUrls = ["https://example.com"],
            AllowedOAuthScopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
        },
    ],
});
```

> The hosted zone for `DomainName` must already exist in the same account. The construct performs a `HostedZone.FromLookup` and creates an ACM certificate with DNS validation automatically.

### Resource Servers and API Scopes

```csharp
var pool = new CognitoUserPoolConstruct(this, "auth-pool", new CognitoUserPoolConstructProps
{
    UserPoolName = "my-api-users",
    Domain = new CognitoUserPoolDomainProps { CognitoDomainPrefix = "my-api-auth" },
    ResourceServers =
    [
        new CognitoResourceServerProps
        {
            Name = "My API",
            Identifier = "my-api",
            Scopes =
            [
                new CognitoResourceServerScopeProps { Name = "read",  Description = "Read access" },
                new CognitoResourceServerScopeProps { Name = "write", Description = "Write access" },
            ],
        },
    ],
    AppClients =
    [
        new CognitoUserPoolAppClientProps
        {
            Name = "my-web-app",
            CallbackUrls = ["https://example.com/callback"],
            LogoutUrls = ["https://example.com"],
            AllowedOAuthScopes =
            [
                OAuthScope.OPENID,
                OAuthScope.EMAIL,
                OAuthScope.Custom("my-api/read"),
            ],
        },
    ],
});
```

### Machine-to-Machine (Client Credentials) App Client

```csharp
new CognitoUserPoolAppClientProps
{
    Name = "backend-service",
    GenerateSecret = true,
    CallbackUrls = [],
    LogoutUrls = [],
    AuthorizationCodeGrant = false,
    ImplicitCodeGrant = false,
    ClientCredentials = true,
    AllowedOAuthScopes = [OAuthScope.Custom("my-api/read")],
    SupportedIdentityProviders = [],
},
```

### User Groups

```csharp
var pool = new CognitoUserPoolConstruct(this, "auth-pool", new CognitoUserPoolConstructProps
{
    UserPoolName = "my-app-users",
    Groups =
    [
        new CognitoUserPoolGroupProps(Name: "admin",  Description: "Administrators", Precedence: 1),
        new CognitoUserPoolGroupProps(Name: "player", Description: "Regular players",  Precedence: 2),
    ],
    // ... domain and app clients
});
```

### Managed Login Branding

```csharp
new CognitoUserPoolAppClientProps
{
    Name = "my-web-app",
    CallbackUrls = ["https://example.com/callback"],
    LogoutUrls = ["https://example.com"],
    AllowedOAuthScopes = [OAuthScope.OPENID, OAuthScope.EMAIL],
    ManagedLoginBranding = new CognitoManagedLoginBrandingProps(
        SettingsJson: MyBrandingConstants.SettingsJson),
},
```

The `SettingsJson` must be a valid Cognito Managed Login settings JSON document. The construct deserializes it and converts it to a JSII-compatible CLR object graph before passing it to CloudFormation.

## CloudFormation Outputs

The construct automatically creates CloudFormation outputs for cross-stack sharing. Export names follow the pattern `{stack-name}-{construct-id}-{qualifier}` (all lowercase):

| Qualifier | Value |
|-----------|-------|
| `user-pool-id` | `UserPool.UserPoolId` |
| `user-pool-arn` | `UserPool.UserPoolArn` |
| `client-{clientName}-id` | `UserPoolClientId` for each app client |

> Spaces in client names are replaced with hyphens and the name is lowercased. For example, a client named `My Web App` produces the qualifier `client-my-web-app-id`.

### Importing in Another Stack

```csharp
// In the consuming stack:
var userPoolId = Fn.ImportValue("my-infra-stack-prod-auth-pool-user-pool-id");
var clientId   = Fn.ImportValue("my-infra-stack-prod-auth-pool-client-my-web-app-id");
```

## Testing

### Props Builder

```csharp
var props = new CognitoUserPoolConstructPropsBuilder()
    .WithUserPoolName("my-pool")
    .WithSelfSignUpEnabled(true)
    .WithCognitoDomain("my-pool-auth")
    .AddResourceServer("My API", "my-api",
        scopes: [new CognitoResourceServerScopeProps { Name = "read", Description = "Read" }])
    .AddWebAppClient(
        name: "web-app",
        callbackUrls: ["https://example.com/callback"],
        logoutUrls: ["https://example.com"])
    .AddGroup("admin", description: "Admins", precedence: 1)
    .Build();
```

#### Convenience: `ForWebApplication`

```csharp
var props = new CognitoUserPoolConstructPropsBuilder()
    .ForWebApplication(userPoolName: "my-pool", cognitoDomainPrefix: "my-pool-auth")
    .Build();
```

This configures a pool with a Cognito-hosted domain and a single `web-client` app client in one call.

### Assertion Methods

```csharp
// Pool and domain
template.ShouldHaveUserPool("my-app-users");
template.ShouldHaveCognitoUserPoolDomain("my-pool-auth");

// App clients and resource servers
template.ShouldHaveUserPoolClient("web-app");
template.ShouldHaveResourceServer("my-api");

// Groups
template.ShouldHaveUserPoolGroup("admin");

// Managed Login branding
template.ShouldHaveManagedLoginBranding();
template.ShouldNotHaveManagedLoginBranding();

// CloudFormation exports
template.ShouldExportUserPoolId("test-stack", "auth-pool");
template.ShouldExportUserPoolArn("test-stack", "auth-pool");
template.ShouldExportAppClientId("test-stack", "auth-pool", "web-app");
```

### AutoFixture Integration

```csharp
[Theory]
[CognitoUserPoolConstructAutoData]
public void Should_Create_User_Pool(CognitoUserPoolConstructProps props)
{
    // props generated with sensible defaults (no branding)
}

[Theory]
[CognitoUserPoolConstructAutoData(includeBranding: true)]
public void Should_Create_User_Pool_With_Branding(CognitoUserPoolConstructProps props)
{
    // props generated with Managed Login branding included
}
```

## Examples

For more real-world examples, see the [Examples](../examples/index.md) section.

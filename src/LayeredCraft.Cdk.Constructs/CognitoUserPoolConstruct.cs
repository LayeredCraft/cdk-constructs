using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Constructs;
using LayeredCraft.Cdk.Constructs.Extensions;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

public sealed class CognitoUserPoolConstruct : Construct
{
    public CognitoUserPoolConstruct(
        Construct scope,
        string id,
        ICognitoUserPoolConstructProps props)
        : base(scope, id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(props);
        ArgumentException.ThrowIfNullOrWhiteSpace(props.UserPoolName);

        UserPool = new UserPool(this, id, new UserPoolProps
        {
            UserPoolName = props.UserPoolName,
            SelfSignUpEnabled = props.SelfSignUpEnabled,
            SignInAliases = new SignInAliases
            {
                Email = true,
            },
            SignInCaseSensitive = false,
            AutoVerify = new AutoVerifiedAttrs
            {
                Email = true,
            },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute
                {
                    Required = true,
                    Mutable = true,
                },
                Fullname = new StandardAttribute
                {
                    Required = true,
                    Mutable = true
                }
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = props.PasswordMinLength,
                RequireLowercase = false,
                RequireUppercase = false,
                RequireDigits = false,
                RequireSymbols = false,
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            Mfa = props.Mfa,
            RemovalPolicy = props.RemovalPolicy,
        });

        var resourceServers = CreateResourceServers(props);

        ResourceServers = resourceServers;

        if (props.Domain is not null)
        {
            ConfigureUserPoolDomain(id, props);
        }

        var appClients = CreateAppClients(props, resourceServers);

        AppClients = appClients;

        CreateGroups(UserPool, props.Groups);

        CreateOutputs(id, appClients);
    }

    public UserPool UserPool { get; }

    public UserPoolDomain? Domain { get; set; }

    public ICertificate? Certificate { get; set; }

    public IReadOnlyDictionary<string, UserPoolResourceServer> ResourceServers { get; }

    public IReadOnlyDictionary<string, UserPoolClient> AppClients { get; }

    private Dictionary<string, UserPoolResourceServer> CreateResourceServers(ICognitoUserPoolConstructProps props)
    {
        var resourceServers = new Dictionary<string, UserPoolResourceServer>(StringComparer.Ordinal);

        foreach (var resourceServer in props.ResourceServers)
        {
            ArgumentNullException.ThrowIfNull(resourceServer);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceServer.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceServer.Identifier);
            ArgumentNullException.ThrowIfNull(resourceServer.Scopes);

            var scopes = resourceServer.Scopes
                .Select(scopeProps =>
                {
                    ArgumentNullException.ThrowIfNull(scopeProps);
                    ArgumentException.ThrowIfNullOrWhiteSpace(scopeProps.Name);
                    ArgumentException.ThrowIfNullOrWhiteSpace(scopeProps.Description);

                    return new ResourceServerScope(new ResourceServerScopeProps
                    {
                        ScopeName = scopeProps.Name,
                        ScopeDescription = scopeProps.Description,
                    });
                })
                .ToArray();

            var createdResourceServer = UserPool.AddResourceServer(
                $"{resourceServer.Name}-resource-server",
                new UserPoolResourceServerOptions
                {
                    Identifier = resourceServer.Identifier,
                    UserPoolResourceServerName = resourceServer.Name,
                    Scopes = scopes,
                });

            resourceServers.Add(resourceServer.Identifier, createdResourceServer);
        }

        return resourceServers;
    }

    private Dictionary<string, UserPoolClient> CreateAppClients(ICognitoUserPoolConstructProps props,
        Dictionary<string, UserPoolResourceServer> resourceServers)
    {
        var appClients = new Dictionary<string, UserPoolClient>(StringComparer.Ordinal);

        foreach (var appClient in props.AppClients)
        {
            ArgumentNullException.ThrowIfNull(appClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(appClient.Name);
            ArgumentNullException.ThrowIfNull(appClient.CallbackUrls);
            ArgumentNullException.ThrowIfNull(appClient.LogoutUrls);
            ArgumentNullException.ThrowIfNull(appClient.AllowedOAuthScopes);
            ArgumentNullException.ThrowIfNull(appClient.SupportedIdentityProviders);

            var createdAppClient = UserPool.AddClient(
                $"{appClient.Name}-app-client",
                new UserPoolClientOptions
                {
                    UserPoolClientName = appClient.Name,
                    GenerateSecret = appClient.GenerateSecret,
                    SupportedIdentityProviders = appClient.SupportedIdentityProviders.ToArray(),
                    OAuth = new OAuthSettings
                    {
                        CallbackUrls = appClient.CallbackUrls.ToArray(),
                        LogoutUrls = appClient.LogoutUrls.ToArray(),
                        Flows = new OAuthFlows
                        {
                            AuthorizationCodeGrant = appClient.AuthorizationCodeGrant,
                            ImplicitCodeGrant = appClient.ImplicitCodeGrant,
                            ClientCredentials = appClient.ClientCredentials,
                        },
                        Scopes = appClient.AllowedOAuthScopes.ToArray(),
                    },
                });
            foreach (var resourceServer in resourceServers)
            {
                createdAppClient.Node.AddDependency(resourceServer.Value);
            }

            CreateManagedLoginBranding(UserPool, createdAppClient, appClient.ManagedLoginBranding, appClient.Name);

            appClients.Add(appClient.Name, createdAppClient);
        }

        return appClients;
    }

    /// <summary>
    /// Creates CloudFormation outputs for the user pool ID, ARN, and each app client ID.
    /// Export names follow the pattern <c>{stack-name}-{construct-id}-{qualifier}</c> produced by
    /// <see cref="StackExtensions.CreateExportName"/>.
    /// </summary>
    private void CreateOutputs(string id, Dictionary<string, UserPoolClient> appClients)
    {
        var stack = Stack.Of(this);

        _ = new CfnOutput(this, $"{id}-user-pool-id-output", new CfnOutputProps
        {
            Value = UserPool.UserPoolId,
            ExportName = stack.CreateExportName(id, "user-pool-id"),
        });

        _ = new CfnOutput(this, $"{id}-user-pool-arn-output", new CfnOutputProps
        {
            Value = UserPool.UserPoolArn,
            ExportName = stack.CreateExportName(id, "user-pool-arn"),
        });

        foreach (var (clientName, client) in appClients)
        {
            var sanitizedName = clientName.ToLowerInvariant().Replace(' ', '-');
            _ = new CfnOutput(this, $"{id}-client-{sanitizedName}-id-output", new CfnOutputProps
            {
                Value = client.UserPoolClientId,
                ExportName = stack.CreateExportName(id, $"client-{sanitizedName}-id"),
            });
        }
    }

    /// <summary>
    /// Creates a <c>AWS::Cognito::ManagedLoginBranding</c> resource for the given app client.
    /// No-ops when <paramref name="branding"/> is <see langword="null"/>.
    /// When a <see cref="Domain"/> has been configured the branding resource is given an explicit
    /// CloudFormation dependency on the domain so it is not created before the domain exists.
    /// </summary>
    private void CreateManagedLoginBranding(
        IUserPool userPool,
        UserPoolClient userPoolClient,
        ICognitoManagedLoginBrandingProps? branding,
        string appClientName)
    {
        if (branding is null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(branding.SettingsJson);

        var parsed = JsonSerializer.Deserialize<JsonElement>(branding.SettingsJson);
        var settings = ToJsiiCompatible(parsed);

        List<CfnManagedLoginBranding.AssetTypeProperty>? assets = null;

        if (branding.Assets is { Count: > 0 })
        {
            assets = [];

            foreach (var asset in branding.Assets)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(asset.Category);
                ArgumentException.ThrowIfNullOrWhiteSpace(asset.ColorMode);
                ArgumentException.ThrowIfNullOrWhiteSpace(asset.Extension);
                ArgumentException.ThrowIfNullOrWhiteSpace(asset.FilePath);

                var bytes = Convert.ToBase64String(File.ReadAllBytes(asset.FilePath));

                assets.Add(new CfnManagedLoginBranding.AssetTypeProperty
                {
                    Category = asset.Category,
                    ColorMode = asset.ColorMode,
                    Extension = asset.Extension,
                    Bytes = bytes,
                });
            }
        }

        var cfnBranding = new CfnManagedLoginBranding(this, $"managed-login-branding-{appClientName}", new CfnManagedLoginBrandingProps
        {
            UserPoolId = userPool.UserPoolId,
            ClientId = userPoolClient.UserPoolClientId,
            Settings = settings,
            Assets = assets?.ToArray(),
        });

        if (Domain is not null)
        {
            cfnBranding.Node.AddDependency(Domain);
        }
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> into a plain CLR object graph
    /// (<see cref="Dictionary{TKey,TValue}"/>, <see cref="Array"/>, or a primitive) that the
    /// JSII runtime can serialize when passing the branding settings to CloudFormation.
    /// <see cref="JsonElement"/> itself is not a JSII-compatible type.
    /// </summary>
    private static object ToJsiiCompatible(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToJsiiCompatible(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ToJsiiCompatible)
                .ToArray(),
            JsonValueKind.String => (object)element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l)
                ? (object)l
                : element.GetDouble(),
            JsonValueKind.True => (object)true,
            JsonValueKind.False => (object)false,
            _ => (object)null!,
        };
    }

    private void ConfigureUserPoolDomain(string id, ICognitoUserPoolConstructProps props)
    {
        var hasPrefixDomain = !string.IsNullOrWhiteSpace(props.Domain!.CognitoDomainPrefix);
        var hasCustomDomain =
            !string.IsNullOrWhiteSpace(props.Domain.DomainName) &&
            !string.IsNullOrWhiteSpace(props.Domain.AuthSubDomain);

        if (hasPrefixDomain == hasCustomDomain)
        {
            throw new ArgumentException(
                "Exactly one domain mode must be configured. Specify either CognitoDomainPrefix or DomainName + AuthSubDomain.",
                nameof(props));
        }

        if (hasPrefixDomain)
        {
            Domain = new UserPoolDomain(this, $"{id}-domain", new UserPoolDomainProps
            {
                UserPool = UserPool,
                CognitoDomain = new CognitoDomainOptions
                {
                    DomainPrefix = props.Domain.CognitoDomainPrefix!,
                },
                ManagedLoginVersion = props.Domain.ManagedLoginVersion switch
                {
                    CognitoManagedLoginVersion.ClassicHostedUi => ManagedLoginVersion.CLASSIC_HOSTED_UI,
                    _ => ManagedLoginVersion.NEWER_MANAGED_LOGIN,
                },
            });
        }
        else
        {
            var zone = HostedZone.FromLookup(this, $"{id}-hosted-zone", new HostedZoneProviderProps
            {
                DomainName = props.Domain.DomainName!,
            });

            var authDomain = $"{props.Domain.AuthSubDomain}.{props.Domain.DomainName}";

            // Use a caller-supplied certificate (required when the stack is not in us-east-1,
            // since Cognito custom domains require an ACM certificate in us-east-1).
            // When none is provided, create one in the stack's region (valid for us-east-1 stacks).
            if (props.Domain.Certificate is null)
            {
                var stackRegion = Stack.Of(this).Region;
                if (!Token.IsUnresolved(stackRegion) && stackRegion != "us-east-1")
                {
                    throw new ArgumentException(
                        $"Cognito custom domains require an ACM certificate in us-east-1, but this stack is in '{stackRegion}'. " +
                        "Create the certificate in a us-east-1 stack and supply it via props.Domain.Certificate.",
                        nameof(props));
                }
            }

            Certificate = props.Domain.Certificate ?? new Certificate(this, $"{id}-certificate", new CertificateProps
            {
                DomainName = authDomain,
                Validation = CertificateValidation.FromDns(zone),
            });

            Domain = new UserPoolDomain(this, $"{id}-domain", new UserPoolDomainProps
            {
                UserPool = UserPool,
                CustomDomain = new CustomDomainOptions
                {
                    DomainName = authDomain,
                    Certificate = Certificate,
                },
                ManagedLoginVersion = props.Domain.ManagedLoginVersion switch
                {
                    CognitoManagedLoginVersion.ClassicHostedUi => ManagedLoginVersion.CLASSIC_HOSTED_UI,
                    _ => ManagedLoginVersion.NEWER_MANAGED_LOGIN,
                },
            });

            if (props.Domain.CreateRoute53Record)
            {
                _ = new ARecord(this, $"{id}-alias-record", new ARecordProps
                {
                    Zone = zone,
                    RecordName = authDomain,
                    Target = RecordTarget.FromAlias(new UserPoolDomainTarget(Domain)),
                });
            }
        }
    }

    private void CreateGroups(
        IUserPool userPool,
        IReadOnlyCollection<ICognitoUserPoolGroupProps>? groups)
    {
        if (groups is null || groups.Count == 0)
        {
            return;
        }

        foreach (var group in groups)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(group.Name);

            _ = new CfnUserPoolGroup(this, $"group-{group.Name.ToLowerInvariant()}", new CfnUserPoolGroupProps
            {
                UserPoolId = userPool.UserPoolId,
                GroupName = group.Name,
                Description = group.Description,
                Precedence = group.Precedence,
                RoleArn = group.RoleArn
            });
        }
    }
}
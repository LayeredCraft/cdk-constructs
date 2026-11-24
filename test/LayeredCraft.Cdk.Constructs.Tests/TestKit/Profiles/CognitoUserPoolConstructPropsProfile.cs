using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Selects the optional pieces present in the composed <see cref="CognitoUserPoolConstructProps"/>.
/// </summary>
[Flags]
public enum CognitoUserPoolConstructPropsOptions
{
    None = 0,
    Branding = 1 << 0,
}

/// <summary>Profile configuration for <see cref="CognitoUserPoolConstructPropsProfile"/>.</summary>
public sealed record CognitoUserPoolConstructPropsConfig(CognitoUserPoolConstructPropsOptions Options);

/// <summary>
/// Composes the deterministic, realistic <see cref="CognitoUserPoolConstructProps"/> test data the
/// Cognito user pool construct tests rely on.
/// </summary>
public sealed class CognitoUserPoolConstructPropsProfile(CognitoUserPoolConstructPropsConfig config) : ICompositionProfile
{
    private const string TestSettingsJson = """
        {
          "components": {
            "pageBackground": {
              "darkMode": { "color": "031425ff" },
              "lightMode": { "color": "ffffffff" }
            },
            "primaryButton": {
              "darkMode": { "defaults": { "backgroundColor": "e9c400ff", "textColor": "231c00ff" } }
            }
          }
        }
        """;

    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => CdkTestHelper.CreateCognitoUserPoolPropsBuilder()
            .AddResourceServer(
                name: "Test API",
                identifier: "test-api",
                scopes:
                [
                    new CognitoResourceServerScopeProps { Name = "read", Description = "Read access" },
                ])
            .AddWebAppClient(
                name: "test-web-client",
                callbackUrls: ["https://example.com/callback"],
                logoutUrls: ["https://example.com"],
                branding: config.Options.HasFlag(CognitoUserPoolConstructPropsOptions.Branding)
                    ? new CognitoManagedLoginBrandingProps(TestSettingsJson)
                    : null)
            .AddGroup(name: "test-group", description: "Test group", precedence: 1)
            .Build());
}

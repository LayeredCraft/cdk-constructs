using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

public class CognitoUserPoolConstructCustomization(bool includeBranding = false) : ICustomization
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

    public void Customize(IFixture fixture)
    {
        fixture.Customize<CognitoUserPoolConstructProps>(transform => transform
            .FromFactory(() => BuildProps())
            .OmitAutoProperties());
    }

    private CognitoUserPoolConstructProps BuildProps() =>
        CdkTestHelper.CreateCognitoUserPoolPropsBuilder()
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
                branding: includeBranding ? new CognitoManagedLoginBrandingProps(TestSettingsJson) : null)
            .AddGroup(name: "test-group", description: "Test group", precedence: 1)
            .Build();
}

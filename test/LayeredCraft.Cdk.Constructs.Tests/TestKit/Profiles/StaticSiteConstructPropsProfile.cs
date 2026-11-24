using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Selects the optional domain pieces present in the composed <see cref="StaticSiteConstructProps"/>.
/// </summary>
[Flags]
public enum StaticSiteConstructPropsOptions
{
    None = 0,
    ApiDomain = 1 << 0,
    AlternateDomains = 1 << 1,
}

/// <summary>Profile configuration for <see cref="StaticSiteConstructPropsProfile"/>.</summary>
public sealed record StaticSiteConstructPropsConfig(StaticSiteConstructPropsOptions Options);

/// <summary>
/// Composes the deterministic, realistic <see cref="StaticSiteConstructProps"/> test data the static
/// site construct tests rely on.
/// </summary>
public sealed class StaticSiteConstructPropsProfile(StaticSiteConstructPropsConfig config) : ICompositionProfile
{
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => new StaticSiteConstructProps
        {
            DomainName = "example.com",
            SiteSubDomain = "www",
            AssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site"),
            ApiDomain = config.Options.HasFlag(StaticSiteConstructPropsOptions.ApiDomain) ? "api.example.com" : null,
            AlternateDomains = config.Options.HasFlag(StaticSiteConstructPropsOptions.AlternateDomains)
                ? ["example.com", "alt.example.com"]
                : [],
        });
}

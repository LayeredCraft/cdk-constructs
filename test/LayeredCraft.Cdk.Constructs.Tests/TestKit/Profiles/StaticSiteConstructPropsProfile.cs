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
/// Supplies the deterministic, realistic <see cref="StaticSiteConstructProps"/> test data the static site
/// construct tests rely on. Every member was pinned by the previous customization, so every member is a
/// behavior-defining literal here too.
/// </summary>
public sealed class StaticSiteConstructPropsProfile(StaticSiteConstructPropsConfig config) : ICompositionProfile
{
    // Compono does not provide this because composition populates constructor parameters and required
    // members only; the behavior-defining ApiDomain/AlternateDomains members are optional (non-required)
    // properties, so a registration factory supplies the whole value.
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

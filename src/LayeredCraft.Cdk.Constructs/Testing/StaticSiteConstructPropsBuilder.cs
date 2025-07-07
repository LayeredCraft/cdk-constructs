using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for creating StaticSiteConstructProps instances in tests.
/// Provides sensible defaults and allows customization of specific properties.
/// </summary>
public class StaticSiteConstructPropsBuilder
{
    private string _domainName = "example.com";
    private string _siteSubDomain = "www";
    private string _assetPath = "./test-static-site";
    private string? _apiDomain = null;
    private readonly List<string> _alternateDomains = new();

    /// <summary>
    /// Sets the root domain name.
    /// </summary>
    /// <param name="domainName">The root domain name</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithDomainName(string domainName)
    {
        _domainName = domainName;
        return this;
    }

    /// <summary>
    /// Sets the site subdomain.
    /// </summary>
    /// <param name="siteSubDomain">The site subdomain</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithSiteSubDomain(string siteSubDomain)
    {
        _siteSubDomain = siteSubDomain;
        return this;
    }

    /// <summary>
    /// Sets the asset path for the static website files.
    /// </summary>
    /// <param name="assetPath">The asset path</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithAssetPath(string assetPath)
    {
        _assetPath = assetPath;
        return this;
    }

    /// <summary>
    /// Sets the API domain for proxying API requests.
    /// </summary>
    /// <param name="apiDomain">The API domain</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithApiDomain(string apiDomain)
    {
        _apiDomain = apiDomain;
        return this;
    }

    /// <summary>
    /// Removes the API domain (sets to null).
    /// </summary>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithoutApiDomain()
    {
        _apiDomain = null;
        return this;
    }

    /// <summary>
    /// Adds an alternate domain to the list.
    /// </summary>
    /// <param name="alternateDomain">The alternate domain to add</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithAlternateDomain(string alternateDomain)
    {
        _alternateDomains.Add(alternateDomain);
        return this;
    }

    /// <summary>
    /// Adds multiple alternate domains to the list.
    /// </summary>
    /// <param name="alternateDomains">The alternate domains to add</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithAlternateDomains(params string[] alternateDomains)
    {
        _alternateDomains.AddRange(alternateDomains);
        return this;
    }

    /// <summary>
    /// Clears all alternate domains.
    /// </summary>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder WithoutAlternateDomains()
    {
        _alternateDomains.Clear();
        return this;
    }

    /// <summary>
    /// Configures for a typical blog setup with www and naked domain.
    /// </summary>
    /// <param name="domain">The base domain (e.g., "myblog.com")</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder ForBlog(string domain)
    {
        _domainName = domain;
        _siteSubDomain = "www";
        _alternateDomains.Clear();
        _alternateDomains.Add(domain); // Naked domain as alternate
        return this;
    }

    /// <summary>
    /// Configures for a documentation site setup.
    /// </summary>
    /// <param name="domain">The base domain</param>
    /// <param name="apiDomain">Optional API domain for documentation API</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder ForDocumentation(string domain, string? apiDomain = null)
    {
        _domainName = domain;
        _siteSubDomain = "docs";
        _apiDomain = apiDomain;
        return this;
    }

    /// <summary>
    /// Configures for a SPA (Single Page Application) with API integration.
    /// </summary>
    /// <param name="domain">The base domain</param>
    /// <param name="apiDomain">The API domain for backend services</param>
    /// <returns>The builder instance for method chaining</returns>
    public StaticSiteConstructPropsBuilder ForSinglePageApp(string domain, string apiDomain)
    {
        _domainName = domain;
        _siteSubDomain = "app";
        _apiDomain = apiDomain;
        return this;
    }

    /// <summary>
    /// Builds the StaticSiteConstructProps instance.
    /// </summary>
    /// <returns>The configured StaticSiteConstructProps</returns>
    public StaticSiteConstructProps Build()
    {
        return new StaticSiteConstructProps
        {
            DomainName = _domainName,
            SiteSubDomain = _siteSubDomain,
            AssetPath = _assetPath,
            ApiDomain = _apiDomain,
            AlternateDomains = _alternateDomains.ToArray()
        };
    }
}
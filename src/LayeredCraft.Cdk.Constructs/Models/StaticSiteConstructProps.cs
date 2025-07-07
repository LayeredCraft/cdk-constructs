namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Interface defining configuration properties for StaticSiteConstruct.
/// Contains all required and optional settings for deploying a static website with CloudFront distribution.
/// </summary>
public interface IStaticSiteConstructProps
{
    /// <summary>
    /// The root domain name for the static site (e.g., "example.com").
    /// Must correspond to an existing Route53 hosted zone.
    /// </summary>
    string DomainName { get; set; }
    /// <summary>
    /// The subdomain for the static site (e.g., "www" for "www.example.com").
    /// Combined with DomainName to form the full site domain.
    /// </summary>
    string SiteSubDomain { get; set; }
    /// <summary>
    /// Path to the static website assets that will be deployed to S3.
    /// Can be a directory containing the built website files.
    /// </summary>
    string AssetPath { get; set; }
    /// <summary>
    /// Optional API domain for proxying API requests through CloudFront.
    /// When specified, requests to /api/* will be forwarded to this domain.
    /// </summary>
    string? ApiDomain { get; set; }
    /// <summary>
    /// Additional domain names that should be included in the SSL certificate and CloudFront distribution.
    /// Useful for supporting multiple domains or subdomains pointing to the same site.
    /// </summary>
    string[] AlternateDomains { get; set; }
}

/// <summary>
/// Implementation record for StaticSiteConstruct configuration properties.
/// Provides a concrete implementation of IStaticSiteConstructProps with sensible defaults.
/// </summary>
public record StaticSiteConstructProps : IStaticSiteConstructProps
{
    /// <inheritdoc />
    public required string DomainName { get; set; }
    /// <inheritdoc />
    public required string SiteSubDomain { get; set; }
    /// <inheritdoc />
    public required string AssetPath { get; set; }
    /// <inheritdoc />
    public string? ApiDomain { get; set; }
    /// <inheritdoc />
    public string[] AlternateDomains { get; set; } = [];
}
# Static Site Construct

The `StaticSiteConstruct` provides complete static website hosting with S3, CloudFront CDN, SSL certificates, Route53 DNS management, and optional API proxying.

## :globe_with_meridians: Features

- **:file_cabinet: S3 Website Hosting**: Public S3 website bucket optimized for static sites (auto-delete on stack removal)
- **:zap: CloudFront CDN**: Global content delivery with SPA-friendly 403 handling
- **:lock: SSL Certificates**: DNS-validated certificate for the primary site domain and alternates
- **:globe_with_meridians: Route53 DNS**: DNS record management for primary and alternate domains
- **:arrows_counterclockwise: API Proxy Support**: Optional CloudFront behavior for `/api/*` paths (API domain served directly from origin)
- **:package: Asset Deployment**: Automatic deployment with cache invalidation

## Basic Usage

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    SiteSubDomain = "www",
    AssetPath = "./website-build"
});
}
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `DomainName` | `string` | Root domain name (e.g., "example.com") used for Route53 hosted zone lookup |
| `SiteSubDomain` | `string` | Subdomain to prefix (e.g., `www`). Final site domain is `{SiteSubDomain}.{DomainName}` |
| `AssetPath` | `string` | Path to static website assets that will be uploaded to S3 |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ApiDomain` | `string?` | `null` | Optional API origin for `/api/*` requests (served directly from the origin) |
| `AlternateDomains` | `string[]` | `[]` | Additional domains included in the certificate and DNS records |

## Construct Properties

The `StaticSiteConstruct` exposes the following properties after instantiation:

| Property | Type | Description |
|----------|------|-------------|
| `SiteDomain` | `string` | Fully qualified domain name of the site (e.g., "www.example.com") |

### Accessing Site Domain

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "example.com",
    SiteSubDomain = "www",
    AssetPath = "./website-build"
});

// Access the computed site domain
var domain = site.SiteDomain; // "www.example.com"
```

## Advanced Examples

### Website with Subdomain

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    SiteSubDomain = "www", // Creates www.example.com
    AssetPath = "./website-build"
});
```

### Website with API Proxy

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    ApiDomain = "api.example.com", // Proxies /api/* to api.example.com
    AssetPath = "./website-build"
});
```

### Website with Multiple Domains

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    AlternateDomains = ["www.example.com", "example.org", "www.example.org"],
    AssetPath = "./website-build"
});
```

### Complete Configuration

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    DomainName = "example.com",
    SiteSubDomain = "www",
    ApiDomain = "api.example.com",
    AlternateDomains = ["example.org", "www.example.org"],
    AssetPath = "./website-build"
});
```

## Domain Resolution

The construct automatically resolves domains based on configuration:

### Primary Domain
- Site domain is always `{SiteSubDomain}.{DomainName}`. Root/apex domains are not currently supported.

### Certificate Domains
The SSL certificate includes:
1. Primary site domain (resolved as above)
2. All domains from `AlternateDomains`
   - `ApiDomain` is **not** added to the certificate; the API origin must serve its own valid certificate.

### Route53 Records
A records are created for:
- Primary site domain
- All alternate domains

## CloudFront Configuration

### Default Behavior
- **Origin**: S3 website endpoint (bucket is public; no OAC/OAI)
- **Viewer Protocol**: Allows HTTP and HTTPS (no automatic redirect)
- **Caching**: Optimized for static assets
- **Index Document**: `index.html`

### API Proxy Behavior (if enabled)
- **Path Pattern**: `/api/*`
- **Origin**: API domain
- **Viewer Protocol**: HTTPS only
- **Caching**: Disabled for API requests

### Error Pages
- **403 Errors**: Redirected to `/index.html`

## S3 Bucket Configuration

The S3 bucket is configured with:
- **Public read access** for website hosting (no OAC/OAI)
- **Website hosting** enabled
- **Index document**: `index.html`
- **Error document**: `index.html` (for SPA routing)

## Asset Deployment

The construct automatically:
1. Deploys all assets from `AssetPath` to S3
2. Invalidates CloudFront cache after deployment
3. Maintains proper content types for different file extensions

## Security Considerations

- **HTTPS Enforcement**: CloudFront allows HTTP; add a `ViewerProtocolPolicy.REDIRECT_TO_HTTPS` behavior if strict HTTPS is required
- **Public Access**: S3 bucket is publicly readable because the origin is the website endpoint
- **Certificate Management**: SSL certificates are automatically provisioned for the site domain(s); API domains must present their own valid certificate

## Regional Considerations

- **SSL Certificates**: Must be in `us-east-1` region for CloudFront
- **S3 Bucket**: Can be in any region
- **Route53**: Global service

## Common Use Cases

### Single Page Application (SPA)
```csharp
var spa = new StaticSiteConstruct(this, "SPA", new StaticSiteConstructProps
{
    DomainName = "myapp.com",
    SiteSubDomain = "www",
    AssetPath = "./build" // React/Vue/Angular build output
});
```

### Blog with API
```csharp
var blog = new StaticSiteConstruct(this, "Blog", new StaticSiteConstructProps
{
    DomainName = "myblog.com",
    SiteSubDomain = "www",
    ApiDomain = "api.myblog.com", // For comments, search, etc.
    AssetPath = "./site" // Static site generator output
});
```

### Documentation Site
```csharp
var docs = new StaticSiteConstruct(this, "Docs", new StaticSiteConstructProps
{
    DomainName = "docs.mycompany.com",
    SiteSubDomain = "www",
    AssetPath = "./docs-build" // Documentation generator output
});
```

## Testing

See the [Testing Guide](../testing/index.md) for comprehensive testing utilities and patterns specific to the Static Site construct.

## Examples

For more real-world examples, see the [Examples](../examples/index.md) section.

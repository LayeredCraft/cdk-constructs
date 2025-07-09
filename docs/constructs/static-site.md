---
layout: default
title: Static Site Construct
---

# Static Site Construct

The `StaticSiteConstruct` provides complete static website hosting with S3, CloudFront CDN, SSL certificates, Route53 DNS management, and optional API proxying.

## Features

- **S3 Website Hosting**: Optimized S3 bucket configuration for static websites
- **CloudFront CDN**: Global content delivery with custom error pages
- **SSL Certificates**: Automatic SSL certificate provisioning and management
- **Route53 DNS**: DNS record management for primary and alternate domains
- **API Proxy Support**: Optional CloudFront behavior for `/api/*` paths
- **Asset Deployment**: Automatic deployment with cache invalidation
- **Custom Error Pages**: 404 and 403 error page handling

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
            SiteBucketName = "my-website-bucket",
            DomainName = "example.com",
            AssetPath = "./website-build"
        });
    }
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `SiteBucketName` | `string` | Name of the S3 bucket for website hosting |
| `DomainName` | `string` | Primary domain name (e.g., "example.com") |
| `AssetPath` | `string` | Path to static website assets |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SiteSubDomain` | `string?` | `null` | Subdomain for the site (e.g., "www") |
| `ApiDomain` | `string?` | `null` | API domain for proxy behavior |
| `AlternateDomains` | `string[]` | `[]` | Additional domains to include in certificate |

## Advanced Examples

### Website with Subdomain

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "example.com",
    SiteSubDomain = "www", // Creates www.example.com
    AssetPath = "./website-build"
});
```

### Website with API Proxy

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "example.com",
    ApiDomain = "api.example.com", // Proxies /api/* to api.example.com
    AssetPath = "./website-build"
});
```

### Website with Multiple Domains

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
    DomainName = "example.com",
    AlternateDomains = ["www.example.com", "example.org", "www.example.org"],
    AssetPath = "./website-build"
});
```

### Complete Configuration

```csharp
var site = new StaticSiteConstruct(this, "MySite", new StaticSiteConstructProps
{
    SiteBucketName = "my-website-bucket",
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
- If `SiteSubDomain` is provided: `{SiteSubDomain}.{DomainName}`
- If no subdomain: `{DomainName}`

### Certificate Domains
The SSL certificate includes:
1. Primary domain (resolved as above)
2. All domains from `AlternateDomains` array
3. API domain (if `ApiDomain` is provided)

### Route53 Records
A records are created for:
- Primary domain
- All alternate domains

## CloudFront Configuration

### Default Behavior
- **Origin**: S3 website endpoint
- **Viewer Protocol**: Redirect HTTP to HTTPS
- **Caching**: Optimized for static assets
- **Index Document**: `index.html`

### API Proxy Behavior (if enabled)
- **Path Pattern**: `/api/*`
- **Origin**: API domain
- **Viewer Protocol**: HTTPS only
- **Caching**: Disabled for API requests

### Error Pages
- **404 Errors**: Redirected to `/index.html` (for SPA routing)
- **403 Errors**: Redirected to `/index.html`

## S3 Bucket Configuration

The S3 bucket is configured with:
- **Public read access** for website hosting
- **Website hosting** enabled
- **Index document**: `index.html`
- **Error document**: `index.html` (for SPA routing)
- **CORS configuration** for cross-origin requests

## Asset Deployment

The construct automatically:
1. Deploys all assets from `AssetPath` to S3
2. Invalidates CloudFront cache after deployment
3. Maintains proper content types for different file extensions

## Security Considerations

- **HTTPS Enforcement**: All traffic is redirected to HTTPS
- **Public Access**: S3 bucket allows public read access (required for website hosting)
- **CORS**: Configured to allow necessary cross-origin requests
- **Certificate Management**: SSL certificates are automatically provisioned and renewed

## Regional Considerations

- **SSL Certificates**: Must be in `us-east-1` region for CloudFront
- **S3 Bucket**: Can be in any region
- **Route53**: Global service

## Common Use Cases

### Single Page Application (SPA)
```csharp
var spa = new StaticSiteConstruct(this, "SPA", new StaticSiteConstructProps
{
    SiteBucketName = "my-spa-bucket",
    DomainName = "myapp.com",
    AssetPath = "./build" // React/Vue/Angular build output
});
```

### Blog with API
```csharp
var blog = new StaticSiteConstruct(this, "Blog", new StaticSiteConstructProps
{
    SiteBucketName = "my-blog-bucket",
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
    SiteBucketName = "my-docs-bucket",
    DomainName = "docs.mycompany.com",
    AssetPath = "./docs-build" // Documentation generator output
});
```

## Testing

See the [Testing Guide](../testing/) for comprehensive testing utilities and patterns specific to the Static Site construct.

## Examples

For more real-world examples, see the [Examples](../examples/) section.
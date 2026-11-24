# Private Static Assets Construct

The `PrivateStaticAssetsConstruct` hosts **immutable, versioned web assets** in a private S3 bucket and serves
them through a CloudFront distribution that reaches the bucket with an **origin access control (OAC)**. It is
the private counterpart to [`StaticSiteConstruct`](static-site.md): there is no public website endpoint, no
public bucket access, and no Route53 requirement. Optional custom DNS, SPA routing and error rewrites are off
by default.

## :lock: Features

- **:lock: Private S3 bucket**: Block Public Access fully on, SSE-S3 encryption, SSL-only bucket policy,
  versioning enabled, and no static website endpoint
- **:shield: CloudFront with OAC**: the distribution reaches the bucket only through an origin access control;
  the bucket policy grants `cloudfront.amazonaws.com` `s3:GetObject`, scoped to the distribution's `SourceArn`
- **:closed_lock_with_key: HTTPS-only by default**: the default viewer protocol policy is `https-only`
  (`redirect-to-https` is available)
- **:zap: Caching**: `CachePolicy.CACHING_OPTIMIZED` by default, configurable
- **:package: Immutable deployment**: `BucketDeployment` with a destination key prefix, `Prune` off by default,
  `RetainOnDelete` on by default, cache-control metadata, and optional invalidation paths
- **:globe_with_meridians: Optional custom DNS**: domain names, a certificate (or a hosted-zone lookup) and A/AAAA
  alias records
- **:curly_loop: Optional SPA routing and error rewrites**: off by default

## Basic Usage

```csharp
using Amazon.CDK;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        var assets = new PrivateStaticAssetsConstruct(this, "GameAssets", new PrivateStaticAssetsConstructProps
        {
            AssetPath = "./dist"
        });

        // assets.Bucket        -> IBucket
        // assets.Distribution  -> IDistribution
        // assets.DomainName    -> the distribution's CloudFront domain name (no custom DNS configured)
    }
}
```

## Immutable, versioned deployment

Deploy immutable game assets under a versioned prefix, keep the previous versions (no prune), retain the
objects when the deployment resource is removed, and serve them with a long-lived immutable cache-control
header:

```csharp
var assets = new PrivateStaticAssetsConstruct(this, "GameAssets", new PrivateStaticAssetsConstructProps
{
    AssetPath = "./dist/game",
    DestinationKeyPrefix = $"game/{buildVersion}/",
    Prune = false,
    RetainOnDelete = true,
    CacheControl =
    [
        CacheControl.SetPublic(),
        CacheControl.MaxAge(Duration.Days(365)),
        CacheControl.Immutable(),
    ],
});
```

This produces objects at `game/<version>/...` with `Cache-Control: public, max-age=31536000, immutable`,
without deleting older versions.

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `AssetPath` | `string` | Path to the asset directory (or file) deployed to the bucket |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DestinationKeyPrefix` | `string?` | `null` | Key prefix in the bucket for the deployed assets |
| `Prune` | `bool` | `false` | Delete objects that are not in the asset during deployment |
| `RetainOnDelete` | `bool` | `true` | Retain deployed objects when the deployment resource is removed |
| `CacheControl` | `CacheControl[]` | `[]` | `Cache-Control` metadata applied to every deployed object |
| `InvalidationPaths` | `string[]?` | `null` | When set (non-empty), invalidate these CloudFront paths after deployment |
| `RemovalPolicy` | `RemovalPolicy` | `RemovalPolicy.RETAIN` | Removal policy for the bucket |
| `ViewerProtocolPolicy` | `ViewerProtocolPolicy` | `HTTPS_ONLY` | Viewer protocol policy for the default behavior |
| `CachePolicy` | `ICachePolicy` | `CachePolicy.CACHING_OPTIMIZED` | Cache policy for the default behavior |
| `Comment` | `string?` | `null` | CloudFront distribution comment |
| `SpaRouting` | `bool` | `false` | Rewrite extensionless paths to `/index.html` for SPAs |
| `ErrorResponses` | `IErrorResponse[]` | `[]` | Custom error responses for the distribution |
| `DefaultRootObject` | `string?` | `null` | Default root object for the distribution |
| `Domain` | `PrivateStaticAssetsDomainOptions?` | `null` | Optional custom DNS configuration |

## Construct Properties

| Property | Type | Description |
|----------|------|-------------|
| `Bucket` | `IBucket` | The private S3 bucket that stores the assets |
| `Distribution` | `IDistribution` | The CloudFront distribution that serves the assets |
| `DomainName` | `string` | The first custom domain name when DNS is configured, otherwise the distribution's CloudFront domain name |

## Custom DNS

Custom DNS is off by default (the CloudFront domain is used). When configured, the distribution gets the domain
names as aliases, and when a hosted zone is available, A and AAAA alias records are created for every domain
name.

```csharp
var assets = new PrivateStaticAssetsConstruct(this, "GameAssets", new PrivateStaticAssetsConstructProps
{
    AssetPath = "./dist/game",
    DestinationKeyPrefix = $"game/{buildVersion}/",
    CacheControl = [CacheControl.SetPublic(), CacheControl.MaxAge(Duration.Days(365)), CacheControl.Immutable()],
    Domain = new PrivateStaticAssetsDomainOptions
    {
        DomainNames = ["assets.example.com", "cdn.example.com"],
        // Either supply a certificate, or a hosted zone (or hosted-zone name) so one can be created.
        HostedZoneName = "example.com",
    },
});
```

`PrivateStaticAssetsDomainOptions`:

| Property | Type | Description |
|----------|------|-------------|
| `DomainNames` | `string[]` | The custom domain names (at least one) |
| `Certificate` | `ICertificate?` | Certificate for the domain names; created (DNS-validated) when omitted. Because CloudFront only accepts viewer certificates from `us-east-1`, the construct rejects certificate creation when the stack region is resolved and is not `us-east-1`; supply a certificate created in `us-east-1` instead. A supplied certificate is accepted in any stack region |
| `HostedZone` | `IHostedZone?` | Hosted zone for validation and records; mutually exclusive with `HostedZoneName` |
| `HostedZoneName` | `string?` | Hosted-zone name looked up with `HostedZone.FromLookup`; mutually exclusive with `HostedZone` |

Validation at construction (`ArgumentException`):

- no domain names;
- both `HostedZone` and `HostedZoneName` set;
- neither a `Certificate` nor a hosted zone (or hosted-zone name) available;
- no `Certificate` and the stack region is resolved and is not `us-east-1` (a CloudFront viewer certificate must
  be created in `us-east-1`; supply a `Certificate` created there). An environment-agnostic stack (unresolved
  region) cannot be checked at synthesis and keeps creating the certificate.

## CloudFront Configuration

- **Origin**: the S3 bucket through an origin access control (`S3BucketOrigin.WithOriginAccessControl`); no
  website endpoint and no public access
- **Viewer protocol**: `https-only` by default (`redirect-to-https` optional)
- **Caching**: `CACHING_OPTIMIZED` by default
- **SPA routing**: off by default; when enabled, a viewer-request CloudFront Function rewrites extensionless
  paths to `/index.html` (requests with a file extension pass through unchanged)
- **Error responses**: none by default
- **Default root object**: none by default

## S3 Bucket Configuration

- **Block Public Access**: fully on
- **Encryption**: SSE-S3 (`AES256`)
- **SSL-only**: a bucket policy denies non-TLS requests
- **Versioning**: enabled
- **No static website endpoint**
- **Removal policy**: `RETAIN` by default
- **No auto-delete objects**: with `RemovalPolicy.DESTROY`, CloudFormation cannot delete the bucket while it
  still holds objects or object versions, so empty it first or keep the default `RETAIN`

## Testing

See the [Testing Guide](../testing/index.md) for comprehensive testing utilities. The library ships
`PrivateStaticAssetsConstructAssertions` (for example `ShouldHavePrivateBucket`, `ShouldHaveOacOnlyOrigin`,
`ShouldScopeBucketPolicyToDistribution`, `ShouldHaveHttpsOnlyViewerPolicy`, `ShouldHaveBucketDeployment`,
`ShouldHaveDnsRecords`) and `PrivateStaticAssetsConstructPropsBuilder`.

## Examples

For more real-world examples, see the [Examples](../examples/index.md) section.

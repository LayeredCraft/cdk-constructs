---
layout: default
title: Web Function Workloads
permalink: /examples/web-functions/
---

# Web Function Workloads

This page documents the three workloads that deploy through the same [Web Function Construct](../constructs/web-function.md) public API: **Next.js**, **ASP.NET Core** (via a Node.js `execve` shim), and **Blazor Static SSR**. The construct is workload-neutral; only the artifact and configuration differ.

!!! warning "What these recipes do and do not prove"
    These recipes describe how each workload is packaged and wired, and the synthesis tests in the repository prove
    that each configuration compiles and synthesizes through the public API. They do **not** prove runtime behavior.
    Actual invocation, shim execution, readiness, and CloudFront end-to-end flows are validated only by a real
    deployment in a sandbox account (see [Sandbox validation](#sandbox-validation)).

    Every recipe also assumes the code artifact is uploaded to a versioned S3 bucket **before** the stack deploys. The
    construct never builds, zips, or uploads code, and an owned bucket created in the same stack does not contain the
    artifact at first deployment (see [S3 Artifact Requirements](../constructs/web-function.md)).

The ASP.NET Core and Blazor recipes rely on a Node.js `execve` shim and a runtime readiness callback that are **not
documented by AWS**; they follow the `lambda-web-function-not-node` reference
([j-d-ha/lambda-web-function-not-node](https://github.com/j-d-ha/lambda-web-function-not-node)).

## Required namespaces

The snippets below are fragments of a stack; they assume the following usings:

```csharp
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
```

## Next.js

Next.js deploys as a **standalone** build.

### Artifact preparation

1. Build with `output: "standalone"` in `next.config.js`.
2. Assemble the zip with the standalone output at the root, plus `.next/static` and `public/`:
    - `server.js` (the standalone server),
    - `.next/static/**`,
    - `public/**`,
    - an `index.js` at the zip root (see below).

### The `index.js` shim and environment mapping

Lambda Web Functions run the zip's `index.js` by default. The shim:

- reads `AWS_LAMBDA_HTTP_ENDPOINT` and sets `HOSTNAME` and `PORT` so Next.js binds the endpoint the runtime expects,
- adjusts `NODE_PATH` (needed for pnpm layouts),
- handles `SIGTERM` for graceful shutdown,
- then `require("./server.js")`.

Because the shim is `index.js`, no `EntryPoint` is needed; set `EntryPoint` only if you rename the entry file.

### Wiring

```csharp
var web = new WebFunctionConstruct(this, "web", new WebFunctionConstructProps
{
    FunctionName = "acme-web",
    Code = WebFunctionCode.FromBucket(codeBucket, $"{artifactTag}/web.zip", new WebFunctionCodeFromBucketOptions
    {
        ObjectVersion = webZipVersionId,
    }),
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["NODE_ENV"] = "production",
        ["NEXT_TELEMETRY_DISABLED"] = "1",
        ["ACME_API_URL"] = api.DefaultEndpoint.Url,
    },
    Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
});
```

### CloudFront and static assets

Front the endpoint with a CloudFront OAC origin. Cache the content-hashed assets and leave personalized SSR uncached:

```csharp
var origin = WebFunctionOrigin.WithOriginAccessControl(web.DefaultEndpoint);

var distribution = new Distribution(this, "distribution", new DistributionProps
{
    DefaultBehavior = new BehaviorOptions
    {
        Origin = origin,
        ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        CachePolicy = CachePolicy.CACHING_DISABLED,
        OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
    },
    AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
    {
        ["/_next/static/*"] = new BehaviorOptions { Origin = origin, CachePolicy = CachePolicy.CACHING_OPTIMIZED },
    },
    DomainNames = ["app.example.com"],
    Certificate = certificate,
});
```

**Caveats.** Each execution environment has its own ephemeral filesystem and cache, so ISR and Next.js image optimization do not share state across environments. Host-dependent rendering must recover the viewer host through a CloudFront Function that copies it into a custom header (see the [caching and header guidance](../constructs/web-function.md#caching-and-header-guidance)).

## ASP.NET Core

ASP.NET Core runs as a self-contained server binary (`server`) launched by a Node.js `execve` shim.

### Hosting approach and the shim

- Publish the app **self-contained for `linux-arm64`** (the target this recipe is tested against; confirm the architecture your deployment uses); the published apphost is `server`.
- Put an `index.js` shim at the zip root that calls
  `process.execve("/var/task/server", ["/var/task/server"], process.env)` so the .NET process replaces the Node.js process.
- Because the shim is `index.js`, no `EntryPoint` override is needed.

### Application startup and HTTP endpoint

- Configure Kestrel to listen on `http://{AWS_LAMBDA_HTTP_ENDPOINT}` (the runtime-provided endpoint). The runtime
  requires the server to begin listening on that endpoint within **10 seconds** of startup.
- After startup, POST to the runtime readiness API at `http://{AWS_LAMBDA_RUNTIME_API}/2025-09-01/ready`.
- Do **not** call `UseHttpsRedirection()` or add HSTS: TLS terminates at the endpoint.
- The runtime image has **no `libicu`**; either enable globalization-invariant mode or ship an app-local ICU (`Microsoft.ICU.ICU4C.Runtime`).

### Artifact packaging and CloudFront

Publish, zip, and upload like the other workloads. An internal API usually needs **no CloudFront**; callers reach it
directly with `IamAuth` and `GrantInvoke`:

```csharp
var api = new WebFunctionConstruct(this, "api", new WebFunctionConstructProps
{
    FunctionName = "acme-api",
    Code = WebFunctionCode.FromBucket(artifacts, $"{artifactTag}/api.zip", new WebFunctionCodeFromBucketOptions
    {
        BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
    }),
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["Acme__TableName"] = table.TableName,
    },
    MaxConcurrencyPerEnvironment = 16,
    Endpoint = new WebFunctionEndpointOptions
    {
        AuthType = WebFunctionAuthType.IamAuth,
        MaxEnvironments = 10,
        RateLimit = 100,
    },
});
table.GrantReadWriteData(api);

// Same-account caller: the Next.js front end ("web", from the Next.js recipe) invokes the API.
// An identity policy on the caller's role is all that is required.
api.DefaultEndpoint.GrantInvoke(web);
```

## Blazor Static SSR

Blazor **Static SSR** uses the same hosting approach and Node.js `execve` shim as ASP.NET Core, and the same runtime
readiness contract.

- Publish an **untrimmed** server build (Blazor Static SSR renders via reflection, so trimming is unsafe).
- When `AWS_LAMBDA_HTTP_ENDPOINT` is set, set the content root to `AppContext.BaseDirectory` so `wwwroot` and `MapStaticAssets` resolve.
- The zip carries `wwwroot/_framework` (roughly 57 MB zipped, about 136 MB unzipped — under the 512 MB unzipped limit).
- Optional Interactive **WebAssembly** components work over plain HTTP.

Static assets are fingerprinted, so cache them; keep SSR pages uncached:

```csharp
var origin = WebFunctionOrigin.WithOriginAccessControl(web.DefaultEndpoint);

var distribution = new Distribution(this, "distribution", new DistributionProps
{
    DefaultBehavior = new BehaviorOptions
    {
        Origin = origin,
        ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        CachePolicy = CachePolicy.CACHING_DISABLED,
        OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
    },
    AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
    {
        ["/_framework/*"] = new BehaviorOptions { Origin = origin, CachePolicy = CachePolicy.CACHING_OPTIMIZED },
        ["/_content/*"] = new BehaviorOptions { Origin = origin, CachePolicy = CachePolicy.CACHING_OPTIMIZED },
    },
    DomainNames = ["app.example.com"],
    Certificate = certificate,
});
```

!!! danger "Blazor Interactive Server is unsupported"
    Blazor **Interactive Server** (and `Auto`, and legacy Blazor Server) require a SignalR WebSocket with session
    affinity. Web Functions reject protocol upgrades, and long polling does not solve the affinity requirement. Use
    Static SSR (optionally with Interactive WebAssembly) instead.

## Sandbox validation

The repository's synthesis tests prove that all three workloads compile and synthesize through the same public API
with no workload-specific construct, helper, or packaging logic in the library. The following still require a real
AWS deployment to a sandbox account and are **not** claimed as passing here:

- the `execve` shim replacing the Node.js process with the .NET `server`,
- the runtime readiness POST to `http://{AWS_LAMBDA_RUNTIME_API}/2025-09-01/ready`,
- a first request served by each workload,
- CloudFront OAC end-to-end invocation (including the transient `403` window while the distribution deploys).

For more examples, see the [Examples](index.md) page.

# Web Function Construct

The `WebFunctionConstruct` deploys an [AWS Lambda Web Function](https://docs.aws.amazon.com/lambda/latest/dg/lambda-web-functions.html): an ordinary HTTP server packaged as a zip, uploaded to S3, and run behind a managed HTTPS endpoint. It owns one Web Function's lifecycle — the function, its current immutable revision, its execution role and log group (each optional), one default endpoint plus any added with `AddEndpoint`, and the function's **single** resource-based policy.

It is intentionally workload-neutral: Next.js, ASP.NET Core, and Blazor Static SSR deploy through the same public API, and the differences live in the artifacts and configuration only. See the [workload recipes](../examples/web-functions.md).

!!! info "Lambda Web Functions are a new AWS feature"
    CloudFormation exposes them as `AWS::Lambda::WebFunction`, `WebFunctionRevision`, `WebFunctionEndpoint`, and
    `AWS::Lambda::ResourcePolicy`. AWS CDK 2.274.0 has only L1 support; this construct wraps the L1s with secure
    wiring, synthesis-time validation of invalid combinations, and CDK-idiomatic grants.

## :rocket: Features

- **:package: S3-backed artifacts**: Deploy from an existing S3 object with `WebFunctionCode.FromBucket`, with optional object-version pinning.
- **:key: Explicit endpoint authentication**: Every endpoint chooses `IamAuth` or `ApplicationManaged`; there is no default auth type.
- **:link: Endpoint-scoped grants**: `GrantInvoke` grants `lambda:InvokeWebFunctionEndpoint` on a specific endpoint ARN.
- **:globe_with_meridians: CloudFront OAC integration**: `WebFunctionOrigin` returns a CloudFront `IOrigin` that configures origin access control and the required resource-policy statement.
- **:shield: One resource policy per function**: Every resource-policy contribution flows through a single policy owned by the construct.
- **:label: Immutable revisions**: Each deployment creates a new revision; endpoints move to it without interruption.
- **:gear: Configurable role and logs**: Bring your own execution role and log group, or let the construct create them.
- **:test_tube: Consumer test helpers**: Fluent `Template` assertions and a props builder in the `Testing` namespace.

## Intentional Non-Goals (v1)

- Building, publishing, zipping, or uploading artifacts. The S3 object must exist before deployment.
- `WebFunctionCode.FromAsset` (designed for later; not built).
- Weighted revisions, canary or blue-green routing, traffic shifting, or retaining previous revisions.
- Network connectors (VPC egress).
- Whole-function imports (`FromWebFunctionAttributes`); only endpoints can be imported.
- Owning or creating CloudFront distributions, ACM certificates, Route 53 records, or WAF. Composition with those is plain CDK.
- Workload-specific constructs or CloudFront behavior helpers.
- Caller-side request signing (SigV4 in your application) or injecting caller configuration.
- Alarms and dashboards.
- Revision encryption with a customer-managed key (`KmsKeyArn`). Deferred; see [Known Limitations](#known-limitations-and-operational-guidance). S3 artifact encryption (SSE-S3/SSE-KMS on the code bucket) is supported.

## Prerequisites

- **AWS CDK for .NET 2.274.0 or later** (`Amazon.CDK.Lib`). This release adds the enum values the construct needs for OAC signing.
- A region where Lambda Web Functions are available.
- A **versioned** S3 bucket that holds the application zip, and an artifact uploaded before you deploy.
- `aws-cdk` bootstrapped in the account/region if you use CDK assets elsewhere in the same app.

## Basic Usage

```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;

public class MyStack : Stack
{
    public MyStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
    {
        // Artifacts live in a versioned bucket (owned elsewhere or imported by name).
        var artifacts = Bucket.FromBucketName(this, "artifacts", "acme-artifacts");

        var web = new WebFunctionConstruct(this, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(artifacts, "1.2.3/web.zip", new WebFunctionCodeFromBucketOptions
            {
                BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
            }),
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
            },
        });
    }
}
```

## Configuration Properties

### Required Properties

| Property | Type | Description |
|----------|------|-------------|
| `FunctionName` | `string` | Web Function name, 1–64 chars matching `^[A-Za-z0-9](?:[A-Za-z0-9_-]*[A-Za-z0-9])?$` |
| `Code` | `WebFunctionCode` | The artifact source, created with `WebFunctionCode.FromBucket` |
| `Endpoint` | `IWebFunctionEndpointOptions` | The default endpoint; `AuthType` is required |

### Optional Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Runtime` | `WebFunctionRuntime` | `NodeJs24X` | The only supported runtime is `nodejs24.x` |
| `EntryPoint` | `string?` | `null` | Exported as `AWS_LAMBDA_NODEJS_ENTRYPOINT`; defaults to `index.js` in the zip root |
| `EnvironmentVariables` | `IReadOnlyDictionary<string,string>` | `{}` | Environment variables for the revision |
| `Timeout` | `Duration` | `30s` | Revision timeout, 3–900 seconds |
| `MaxConcurrencyPerEnvironment` | `int?` | `null` | Max concurrent **requests per environment**, 1–128 (AWS default 64) |
| `Description` | `string?` | `null` | Revision description |
| `Role` | `IRole?` | `null` | Execution role; when `null` a role with `AWSLambdaBasicExecutionRole` is created |
| `Logs` | `WebFunctionLogs` | `WebFunctionLogs.Create()` | Where revision logs go |
| `ApplicationLogLevel` | `ApplicationLogLevel?` | `null` | Application log level for the revision |
| `SystemLogLevel` | `SystemLogLevel?` | `null` | System log level for the revision |

### Endpoint options (`WebFunctionEndpointOptions`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EndpointName` | `string` | `"default"` | Unique per function, 1–64 chars |
| `AuthType` | `WebFunctionAuthType` | *(required)* | `IamAuth` or `ApplicationManaged` |
| `EndpointType` | `WebFunctionEndpointType` | `HomeRegion` | `HomeRegion`, `MultiRegion`, or `PerRegion` |
| `Regions` | `IReadOnlyList<string>` | `[]` | Required for `MultiRegion`/`PerRegion`; at least one non-home region |
| `MaxEnvironments` | `int?` | `null` | 2–10000; `null` means the account vCPU quota only |
| `RateLimit` | `int?` | `null` | `0`, `100..1000` step 100, or `2000..10000` step 1000 |
| `Description` | `string?` | `null` | Endpoint description |

## :package: S3 Artifact Requirements

`WebFunctionCode.FromBucket(bucket, key, options)` is the only supported source. The bucket **must have versioning enabled**, and the object must exist before CloudFormation creates the revision — the construct cannot verify that at synthesis time.

- **Version pinning**: set `WebFunctionCodeFromBucketOptions.ObjectVersion` to pin an exact object version. If you omit it, Lambda pins the latest version at revision-creation time.
- **Read access**: Lambda reads the artifact with the **Lambda service principal** (`lambda.amazonaws.com`), not with the caller. When the construct manages the policy (`Automatic`) the statement grants `s3:GetObject` and `s3:GetObjectVersion` and is scoped with **both** `StringEquals aws:SourceAccount` (the function's account) **and** `ArnLike aws:SourceArn` of the form `arn:aws:lambda:{region}:{account}:web-function/{FunctionName}/revision/*`. An externally managed policy must grant at least those actions with equivalent scoping.
- **`Automatic` vs `ExternallyManaged`**:
    - `Automatic` (default) adds that statement and makes the revision depend on the resulting bucket policy. It manages a bucket **owned in this app**, including a bucket owned by **another stack when that stack's account and region are explicitly resolvable** (for example a stack created with an explicit `Env`). If the bucket is imported, or is owned by another stack whose required environment information cannot be resolved, `AddToResourcePolicy` reports that nothing was added and synthesis fails asking you to switch to `ExternallyManaged`.
    - `ExternallyManaged` adds nothing; the bucket's owner must already grant the service-principal read (for example a long-lived artifact bucket deployed and managed in its own stack).
- **SSE-KMS**: if the code bucket uses SSE-KMS, its key policy must allow `lambda.amazonaws.com` `kms:Decrypt`, because Lambda reads with its service principal. CDK-managed `aws/s3` and DSSE-managed keys cannot grant that, so an owned bucket using them is rejected. This is separate from the revision's own `KmsKeyArn`, which is not supported in v1.
- **Cross-account / cross-region buckets**: allowed within the same partition (not an opt-in region). Cross-account access needs a bucket-policy grant.
- **Object lifecycle**: deleting the pinned version (or removing read access) moves revisions to permanent `Failed`; requests then receive `409 Platform.RevisionNotActive`.
- **CDK bootstrap bucket is not usable**: the bootstrap asset bucket does not grant the service-principal read, which is why v1 is `FromBucket` only.

!!! warning "The artifact must exist before you deploy"
    The object must exist before CloudFormation creates the revision, and a bucket created in the **same stack** as
    the function does not contain it at first deployment. In practice the artifact is uploaded **before** you deploy
    the function stack. A separately deployed artifact bucket is a good fit (deploy the bucket stack, upload the
    artifact, then deploy the function stack), but any bucket you populate first — including a runtime upload from a
    pipeline or a bucket shared across applications — works equally well.

## :shield: Execution Role and Logging

- **Role**: when `Role` is `null`, the construct creates a role with the `AWSLambdaBasicExecutionRole` managed policy, which includes the CloudWatch Logs write permissions. When you supply a role, the construct uses it as-is and does **not** attach the managed policy **or** grant it log-write permissions — make sure a supplied role can write to wherever its logs go (for example by letting the log group's `GrantWrite` apply, or by granting `logs:CreateLogStream` and `logs:PutLogEvents` yourself).
- **Logs** (`WebFunctionLogs`):
    - `WebFunctionLogs.Create(retention, removalPolicy)` — the default. Creates `/aws/lambda/web/{FunctionName}` with `RetentionDays.TWO_WEEKS` and `RemovalPolicy.DESTROY`. The name matches Lambda's own default group, so switching between `Create()` and `ServiceDefault()` never moves your logs. Because the default removal policy is `DESTROY`, switching from `Create()` to `ServiceDefault()` deletes the stack-owned log group; Lambda then recreates its default group (with no expiry) on the next invocation. Use `Use(LogGroup.FromLogGroupName(...))` to keep a group under your own lifecycle.
    - `WebFunctionLogs.ServiceDefault()` — uses the Lambda-provided default group with no expiry and creates no `AWS::Logs::LogGroup`.
    - `WebFunctionLogs.Use(logGroup)` — routes logs to an existing group. The group is referenced by name, and the execution role is granted `logs:GrantWrite` when the group name is outside `/aws/lambda/*`.
- **Log levels**: `ApplicationLogLevel` and `SystemLogLevel` reuse the CDK types `Amazon.CDK.AWS.Lambda.ApplicationLogLevel` and `SystemLogLevel`.

## Function Revisions and Endpoints

### Revisions

Every deploy creates a new **immutable revision**. Changing the code key/version, environment variables, timeout, role, logging, or description produces a new revision and moves every endpoint to it without interruption. The previous revision is deleted during cleanup. Rollback is a redeploy of an earlier artifact (which creates another new revision); there is no re-selection of an old revision.

### Endpoints

The construct always creates a `DefaultEndpoint` (endpoint name `default`) and exposes it as the `DefaultEndpoint` property. Add more with `AddEndpoint(id, options)`; each added endpoint has identical semantics to the default one. Endpoint names must be unique per function, so passing `EndpointName = "default"` to `AddEndpoint` fails as a duplicate.

`AddEndpoint` rejects the reserved construct IDs `Function`, `Revision`, `DefaultEndpoint`, `ExecutionRole`, `LogGroup`, and `ResourcePolicy` with an `ArgumentException`.

```csharp
var webhooks = web.AddEndpoint("webhooks", new WebFunctionEndpointOptions
{
    EndpointName = "webhooks",
    AuthType = WebFunctionAuthType.ApplicationManaged,
    RateLimit = 100,
});
```

To change an endpoint's `EndpointName`, `EndpointType`, or `Regions` you cannot update in place — CloudFormation would try to replace a fixed name and fail with "already exists", and the domain changes. Add a new endpoint, move consumers, then remove the old one.

## Authentication Modes

`AuthType` is a decision you make on every endpoint:

| Mode | Who can reach the endpoint |
|------|----------------------------|
| `IamAuth` | Only callers that SigV4/SigV4A-sign with `lambda:InvokeWebFunctionEndpoint` and are authorized by an identity policy and/or the function's resource policy. |
| `ApplicationManaged` | **Anyone who knows the URL.** Lambda does not authenticate the request or evaluate the resource policy. The endpoint stays publicly reachable at its own domain, **even behind CloudFront**. |

!!! warning "IamAuth is not user authentication"
    `IamAuth` is a transport boundary between AWS principals. It never identifies an end user. Lambda strips the
    SigV4 `Authorization` header before your server sees it, so application authentication (cookies, JWTs, session
    tokens) is entirely your server's responsibility. With a CloudFront OAC origin, the **viewer's** `Authorization`
    header passes through unchanged.

ApplicationManaged is appropriate when your application authenticates every request itself (for a genuinely public API). IamAuth is appropriate for service-to-service calls and for endpoints fronted by CloudFront with OAC.

## Invocation Grants

Use `GrantInvoke` on an endpoint to authorize callers. An endpoint is an `IResource` and exposes:

- `endpoint.GrantInvoke(grantee)` — the default `Automatic` caller policy.
- `endpoint.GrantInvoke(grantee, new WebFunctionInvokeGrantOptions { CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged })`.
- `endpoint.GrantCloudFrontInvoke(distributionArn)` — CloudFront-specific (see [CloudFront OAC](#cloudfront-oac-integration)).

`GrantInvoke` throws for an `ApplicationManaged` endpoint: there is no IAM boundary to grant against.

### Identity policy vs resource policy

- A **same-account** caller needs only an **identity policy** (an `AWS::IAM::Policy` attached to the caller) allowing `lambda:InvokeWebFunctionEndpoint` on the endpoint ARN.
- A **cross-account** caller needs the identity policy **and** a statement in the function's **resource policy**.

`GrantInvoke` writes the correct side for you whenever it can establish ownership, and fails synthesis when it cannot, rather than silently producing a nonfunctional grant.

### Owned versus imported principals

The construct distinguishes two kinds of grantee:

- **Owned mutable identities** — `Role`, `User`, `Group`, or `LazyRole` **created in this app** (for example, a `Role` L2 or a `WebFunctionConstruct` itself). These can receive an identity policy.
- **Everything else** — imported roles/users (`Role.FromRoleArn`), principals without a concrete owned identity, and any principal whose account cannot be established. Whether an identity policy may be written depends on the rules below.

### Same-stack, environment-agnostic grants

`Automatic` grants are valid when the owned caller and the endpoint belong to the **same `Stack` instance**, even when that stack's environment is unresolved. This is specifically about a single stack (or one stack reference) — two **separate** env-agnostic stacks are *not* the same stack. Their accounts are unresolved, so an `Automatic` grant between them is rejected; use `ExternallyManaged` across such stacks.

### Cross-stack and cross-account restrictions

`Automatic` (the default) requires the library to fully configure invocation within this deployment. It rejects, at synthesis:

- Two different stacks where either stack's account is unresolved.
- Two different stacks with different literal accounts.
- Cross-account imported roles.
- Any grantee whose account cannot be established.

For these cases use `ExternallyManaged` (below) and configure the caller's identity policy in the caller's own account/stack.

### Automatic versus ExternallyManaged caller policies

| `CallerPolicy` | Behavior |
|----------------|----------|
| `Automatic` | The library must write a working grant. If it cannot fully configure invocation, **synthesis fails** with a message naming the grantee, the endpoint, the reason, and two remedies. |
| `ExternallyManaged` | The library writes **only** the endpoint's resource-policy statement (if the endpoint is owned) and adds an info annotation. You are responsible for the caller's identity policy. |

!!! warning "ExternallyManaged does not mean permissions are configured"
    Selecting `ExternallyManaged` does **not** grant anything on the caller's side. It records the resource-policy
    statement and annotates that *Invocation by {principal} requires an identity policy in the caller's account
    allowing `lambda:InvokeWebFunctionEndpoint` on {endpoint ARN}.* Invocation fails until that policy exists.

### Why GrantInvoke rejects certain principals

To avoid confused-deputy grants and false confidence, `GrantInvoke` fails synthesis for:

- **Service principals** — bare or conditioned. An unconditioned service-principal statement would let, for example, *any* distribution in *any* account with an OAC origin for this endpoint invoke it. Use `GrantCloudFrontInvoke(distributionArn)` for CloudFront, or an explicit condition-scoped `AddToResourcePolicy` statement for any other service, including the scoping condition that service documents. `GrantInvoke` cannot know which condition keys a service supplies, so it neither adds `aws:SourceAccount` nor accepts arbitrary conditions as proof of scoping.
- **Account principals** (`AccountPrincipal`, `AccountRootPrincipal`, and any principal whose literal AWS principal is an account-root ARN), including the same account. An account-root resource statement authorizes no specific caller by itself, so it is never treated as a complete `Automatic` grant. Under `ExternallyManaged` an explicitly requested account principal *can* be recorded in the owned endpoint's resource policy, but that is only half the picture — caller-side authorization is still not configured, and the result must not be described as a working grant.
- **Wildcard principals** (conditioned or not) — `AnyPrincipal`, `StarPrincipal`, `OrganizationPrincipal`, `*`. Conditioned wildcards are supported only through an explicit `AddToResourcePolicy` statement (see below). `AddToResourcePolicy` only checks that an Allow wildcard statement **has** conditions — it does **not** verify that those conditions actually restrict access. You own the condition semantics: a condition such as `aws:PrincipalOrgID` can meaningfully restrict who may invoke, but the mere presence of a condition proves nothing, so design and validate the condition yourself.
- **`PrincipalWithConditions`** wrapping any principal, under either caller policy. Grant the underlying principal directly, or use `AddToResourcePolicy`.
- **`Role.WithoutPolicyUpdates()`** and every imported role as an identity-policy target — they are not concrete owned mutable identities, so the library falls back to the resource policy on an owned endpoint, or throws on an imported one.

If a case cannot be safely configured on either side, `GrantInvoke` throws. A false negative (requiring you to act) is always preferred over a false positive.

## Resource-Policy Ownership

A Web Function has **exactly one** resource policy, attached to the function ARN, and the construct owns it. Every contribution flows through that single policy:

- The construct exposes no public "add to function policy" entry point. The only resource-policy entry points are on the **endpoint**: `GrantInvoke`, `GrantCloudFrontInvoke`, and `AddToResourcePolicy`.
- `endpoint.AddToResourcePolicy(statement)` requires `statement.Resources` to be **exactly** that endpoint's ARN; anything else throws an `ArgumentException`.
- Statements are validated when added (a Deny is unrestricted; an Allow must not use `NotPrincipal`; an Allow naming a wildcard or service principal must carry conditions), copied, frozen, and validated as part of the candidate policy document. A rejected statement leaves no partial state.
- Identical statements are deduplicated at synthesis.
- Multiple distributions or endpoints never create competing policy resources.

Because the policy is a single PUT-replace resource, an out-of-band `aws lambda-web put-resource-policy` is overwritten the next time the policy changes, and AWS warns against combining it with `AWS::Lambda::Permission` on the same function. Keep this function's invocation authorization in this construct.

## Imported Endpoints

Use `WebFunctionEndpointConstruct.FromEndpointAttributes` to reference an endpoint owned by another stack or app:

```csharp
var shared = WebFunctionEndpointConstruct.FromEndpointAttributes(this, "shared", new WebFunctionEndpointAttributes
{
    EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
    DomainName = "abc123.lambda-web.us-east-1.on.aws",
    AuthType = WebFunctionAuthType.IamAuth,
});
```

An imported endpoint owns nothing. Its `GrantCloudFrontInvoke` and any grant that would need its resource policy throw — the resource policy is owned externally. `GrantInvoke` with `CallerPolicy = ExternallyManaged` **also** throws on an imported endpoint, because there is no owned resource policy to receive the statement; configure the caller's access in the endpoint owner's policy instead. It is usable as a CloudFront origin with `WebFunctionOriginPermissions.ExternallyManaged`, and it writes identity policies only for an owned mutable caller whose account is literally the same as the endpoint's.

- Literal `EndpointArn` values are validated against `arn:{partition}:lambda:{region}:{12-digit account}:web-function/{function}/endpoint/{endpoint}`; literal `DomainName` must be non-empty; literal `Account` must be 12 digits and agree with a literal ARN's account.
- **Account (`WebFunctionEndpointAttributes.Account`)** is required when `EndpointArn` is a **token** and you want identity grants: the endpoint's account = the literal ARN account, else the literal `Account`, otherwise it is **not established** and `GrantInvoke` throws for every grantee. A token-ARN endpoint can still be used as a CloudFront origin with `ExternallyManaged` permissions.

## CloudFront OAC Integration

`WebFunctionOrigin` is a static factory that returns a CloudFront `IOrigin`, analogous to CDK's `FunctionUrlOrigin`.

```csharp
var origin = WebFunctionOrigin.WithOriginAccessControl(web.DefaultEndpoint, new WebFunctionOriginWithOacProps
{
    CustomHeaders = new Dictionary<string, string> { ["x-origin-verify"] = secret },
});

var distribution = new Distribution(this, "distribution", new DistributionProps
{
    DefaultBehavior = new BehaviorOptions
    {
        Origin = origin,
        ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        AllowedMethods = AllowedMethods.ALLOW_ALL,
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

- `WithOriginAccessControl(endpoint, props)` requires an `IamAuth` endpoint. When bound to a distribution it creates one `FunctionUrlOriginAccessControl` (origin type `lambda`, `always-amz-auth`, `sigv4` for HomeRegion/PerRegion and `sigv4a` for MultiRegion) and adds the resource-policy statement `cloudfront.amazonaws.com → lambda:InvokeWebFunctionEndpoint` on the endpoint ARN with `StringEquals aws:SourceArn = arn:aws:cloudfront::{account}:distribution/{id}`.
- `WithoutOriginAccessControl(endpoint, props)` requires an `ApplicationManaged` endpoint; the endpoint remains directly reachable and CloudFront adds no access control.
- Passing an **`ApplicationManaged` endpoint to the OAC factory**, or an **`IamAuth` endpoint to the non-OAC factory**, throws an `InvalidOperationException` at the factory call.
- **Automatic permissions** (the default) require the distribution and the Web Function to be in the **same stack**; otherwise synthesis fails with a dependency-cycle explanation. For a cross-stack distribution, use `Permissions = WebFunctionOriginPermissions.ExternallyManaged` and grant the distribution ARN via `endpoint.GrantCloudFrontInvoke(...)` in the owner's stack (a two-phase rollout).
- The same origin object reused on several behaviors of one distribution binds once, producing one OAC and one policy statement.
- A supplied `OriginAccessControl` is used unchanged and not inspected; the consumer is responsible for it being `lambda`/`always-amz-auth`/correct protocol.

### Caching and header guidance

- **Never forward the viewer `Host` header.** Use `OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER`, not `ALL_VIEWER`. Forwarding `Host` makes the endpoint return `404`.
- **Dynamic/personalized routes** should use `CACHING_DISABLED`.
- For **GET/HEAD**, CloudFront drops the viewer `Authorization` header unless it is part of the cache policy; prefer cookies or custom headers, and add `Authorization` to a cache policy only when you truly need it.
- **Host-dependent applications** (multi-tenant hosts) must recover the viewer host through a CloudFront Function that copies it into a custom header, and trust that header only after an origin-verification check.
- **Content-hashed static assets** (`/_next/static/*`, Blazor fingerprinted `/_framework/*` files, `/_content/*`) should use `CACHING_OPTIMIZED`.
- Lambda@Edge origin-request/origin-response triggers are **rejected** by CloudFront on behaviors that use a Web Function origin with OAC.
- OAC is **not** supported for global endpoints that include opt-in regions (requests get `403`).
- OAC takes effect only after the distribution deploys (and, for MultiRegion, after the policy replicates); brief `403`s in between are expected.

## Grant Behavior Reference

The table below summarizes `GrantInvoke` behavior for an **owned** endpoint (an endpoint on a function in this app). An imported endpoint is stricter: it owns no resource policy, so only an owned mutable caller whose account is literally the same as the endpoint's can be granted (via an identity policy), and every other case throws.

| Grantee | `Automatic` (default) | `ExternallyManaged` |
|---------|------------------------|---------------------|
| Owned mutable identity (`Role`, `User`, `Group`, `LazyRole` created in this app) — same stack, or different stacks with the same literal account | Complete — identity policy written | Partial by contract — only the resource-policy statement is written; you configure the caller's identity policy |
| `Role.FromRoleArn` (imported) whose literal account equals the endpoint's | Complete — the owned endpoint's resource policy is used | Partial by contract |
| `Role.WithoutPolicyUpdates()` (owned, but not a mutable identity) | Complete via the resource policy (no identity policy written) | Partial by contract |
| Account principal / account-root principal | Synthesis error | Partial by contract — the account principal may appear in the endpoint's resource policy, but caller-side authorization is not configured |
| Service principal (for example `cloudfront.amazonaws.com`) | Synthesis error — use `GrantCloudFrontInvoke` for CloudFront, or a condition-scoped `AddToResourcePolicy` | Synthesis error |
| Wildcard principal (`AnyPrincipal`, `StarPrincipal`, `*`) | Synthesis error | Synthesis error — use an explicit conditioned `AddToResourcePolicy` |
| `PrincipalWithConditions` wrapper | Synthesis error | Synthesis error |
| Cross-account caller (different literal account) | Synthesis error | Partial by contract |
| Caller whose account cannot be established (unresolved) | Synthesis error | Partial by contract |
| Any grantee on an **imported** endpoint | Treated as owned-mutable-same-account only; otherwise synthesis error | Synthesis error (no owned resource policy) |

"Partial by contract" means the library writes the resource-policy statement (on an owned endpoint) and annotates what the caller must do; it never claims the caller side is configured.

## Signing Requests

A direct IAM invocation of an `IamAuth` endpoint must be signed with AWS Signature Version 4:

- **Service name**: `lambda`.
- **Payload hash**: send `x-amz-content-sha256: UNSIGNED-PAYLOAD` (chunked payload signing is not supported).
- **Protocol**: `HomeRegion` and `PerRegion` endpoints use **SigV4** signed for the endpoint's Region; `MultiRegion` endpoints use **SigV4A** with the signing region set to `*`.
- The construct (and CloudFront, when not using OAC) does **not** sign requests on your behalf — the calling SDK or your BFF signs itself.

CloudFront OAC signing is different: with `always-amz-auth`, CloudFront signs the origin request in its own dedicated header and passes the viewer's `Authorization` header through unchanged. Lambda strips its own SigV4 `Authorization` (and `X-Amz-Security-Token`) before forwarding, so application code sees the viewer's header, not the caller's signature.

## Resource Replacement and Update Behavior

| Change | Effect |
|--------|--------|
| Code key/version, environment variables, timeout, execution role, logging, description | Creates a **new revision**; endpoints move to it without interruption; the previous revision is removed during cleanup. |
| `MaxEnvironments`, `RateLimit`, endpoint `Description` | Updated in place (no interruption). Omitting the option keeps the current value; clearing it removes the setting. |
| Endpoint `AuthType` | No interruption, but switching `ApplicationManaged` → `IamAuth` while introducing an OAC can return transient `403`s until the distribution deploys. |
| `EndpointName`, `EndpointType`, `Regions` | **Replacement.** Reusing a fixed name fails with "already exists" and the endpoint domain changes — add a new endpoint, move consumers to it, then remove the old one. |
| `FunctionName` | **Replacement of everything.** Fails while the old name still exists; endpoint domains change. |
| Distribution replacement | The distribution ID changes; the policy statement is updated in the same deployment (no interruption), though the new distribution may return `403` until the policy update propagates. |
| Resource-policy statement changes | The `PolicyDocument` is updated in place (no interruption). |

A deployment that fails is rolled back by CloudFormation to the previous revision. These behaviors follow the CloudFormation resource model in the approved design; anything not listed here should be treated as unverified.

## Public Testing Helpers

The `LayeredCraft.Cdk.Constructs.Testing` namespace ships fluent helpers for synthesized templates:

- `WebFunctionConstructAssertions` extension methods on `Template`: `ShouldHaveWebFunction`, `ShouldHaveRevision`, `ShouldHaveRevisionCode`, `ShouldHaveEndpoint`, `ShouldHaveEndpointScaling`, `ShouldHaveInvokeGrant`, `ShouldHaveSingleResourcePolicy`, `ShouldHaveNoResourcePolicy`, `ShouldAllowCloudFront`, `ShouldAllowResourcePolicyPrincipal`, `ShouldHaveWebFunctionOac`, and `ShouldHaveCodeBucketReadStatement`. They match by function and endpoint **names** (not logical IDs) and require `Effect = Allow` with the expected action and the exact endpoint resource, so a Deny or an unrelated action never satisfies them.
- `WebFunctionConstructPropsBuilder` via `CdkTestHelper.CreateWebFunctionPropsBuilder()`: `WithFunctionName`, `WithCode`, `WithTestArtifact`, `WithIamAuthEndpoint`, `WithApplicationManagedEndpoint`, `WithEndpoint`, `WithEnvironmentVariable`, `WithEntryPoint`, `WithTimeout`, `WithMaxConcurrencyPerEnvironment`, `WithRole`, `WithLogs`, then `Build()`. `Build()` throws without code or an endpoint.

```csharp
var template = Template.FromStack(stack);
template.ShouldHaveWebFunction("acme-web");
template.ShouldHaveEndpoint("acme-web", "default", WebFunctionAuthType.IamAuth);
template.ShouldAllowCloudFront("acme-web", "default");
```

## Workload Recipes

Detailed recipes for Next.js, ASP.NET Core (Node.js `execve` shim), and Blazor Static SSR are in the [Web Function Workloads](../examples/web-functions.md) page. They show how to build each artifact and wire CloudFront, and they are explicit about what synthesis tests verify versus what requires a real AWS deployment.

## Known Limitations and Operational Guidance

- **S3 versioning via escape hatches is not detected.** The owned-bucket inspection only reads literal values in the rendered L2 properties. Versioning enabled through a property override (`CfnBucket`) is not detected; reference such a bucket by name (`Bucket.FromBucketName`) with `ExternallyManaged` instead.
- **Tokenized configuration is skipped, never rejected.** A token versioning status, encryption algorithm, or key ID can be shown neither compliant nor non-compliant, so validation skips it.
- **Log-group switching.** Moving an existing function from `WebFunctionLogs.ServiceDefault()` to `Create()` — or redeploying after a stack deletion while `/aws/lambda/web/{name}` still exists — fails with `AlreadyExists`, because Lambda auto-creates the group. Remedy: use `WebFunctionLogs.Use(LogGroup.FromLogGroupName(...))`, or delete/import the existing group.
- **IAM policy propagation.** The revision depends on the execution role's `CfnRole` only, never the role's default policy, so permissions granted to the role may not have propagated when the revision first activates. This avoids CloudFormation cycles for mutually-referencing functions.
- **CloudFront origin-binding exceptions across jsii.** Validation that runs inside the CDK `IOrigin.Bind` path (and inside `HttpOrigin` construction) surfaces in .NET as a `System.Exception` carrying CDK's message, whereas factory-time checks throw `InvalidOperationException`.
- **F3006 resource-type warnings.** CDK 2.274.0 emits `F3006` "Unknown resource type `AWS::Lambda::WebFunction*`" validation warnings. They are warnings by default and become **synthesis errors** when `@aws-cdk/core:validateAgainstDefaultRules` is enabled; acknowledge with `CloudFormation-Validate::F3006`.
- **OAC naming.** The auto-created OAC name is derived from the distribution's origin scope. OAC names are unique per AWS **account** across **Regions**, so the collision risk is identical names within the *same* account — not across different accounts. If you deploy the same distribution shape repeatedly in one account, verify the generated naming still meets your conventions.
- **Origin request policy.** Use `ALL_VIEWER_EXCEPT_HOST_HEADER`, never `ALL_VIEWER` (see [Caching and header guidance](#caching-and-header-guidance)).
- **Customer-managed revision keys.** `KmsKeyArn` is not supported in v1. The only workaround is an unsupported escape hatch — `((CfnWebFunctionRevision)web.Node.FindChild("Revision")).KmsKeyArn = key.KeyArn` plus the key-policy grants you write.
- **Sandbox validation.** Runtime behavior (actual invocation, execve shims, readiness, CloudFront end-to-end) is validated only by a real deployment in a sandbox account; synthesis tests do not prove runtime support.

## Validation Errors

The construct fails at synthesis with `ArgumentException`, `ArgumentOutOfRangeException`, or `InvalidOperationException`, following repository style. Literal values are validated; tokens (`Token.IsUnresolved`) are skipped, never rejected. Notable cases:

- OAC origin for an `ApplicationManaged` endpoint, or non-OAC origin for an `IamAuth` endpoint → `InvalidOperationException` at the origin factory.
- Automatic OAC permissions with an imported endpoint or a cross-stack distribution → `InvalidOperationException` at bind.
- `GrantInvoke`/`GrantCloudFrontInvoke` on an `ApplicationManaged` endpoint, or a resource-policy grant on an imported endpoint → `InvalidOperationException`.
- An `Automatic` grant that cannot be fully configured → `InvalidOperationException` naming the grantee, endpoint, reason, and remedies.
- `AddToResourcePolicy` with a resource that is not exactly the endpoint ARN, or an unconditioned wildcard/service-principal Allow, or `NotPrincipal` → `ArgumentException`.
- A props-builder `Build()` without code or endpoint → `InvalidOperationException`.

## Testing

See the [Testing Guide](../testing/index.md) for the test helpers and patterns shared across constructs.

## Examples

See the [Examples](../examples/index.md) page and the [Web Function Workloads](../examples/web-functions.md) page.

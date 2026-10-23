# Design: Lambda Web Functions, IAM endpoint authentication, and CloudFront OAC

| | |
|---|---|
| **Status** | **Approved** (revision 3, 2026-10-22) for implementation planning ([#98](https://github.com/LayeredCraft/cdk-constructs/issues/98)). Implementation proceeds through separately planned tasks. |
| **Date** | 2026-10-22 (revisions 2–3 address review feedback; see [§21](#21-revision-history)) |
| **Authority** | This document is the authoritative architectural record for the feature. No standalone ADRs are written for it at this time. |
| **Target package** | `LayeredCraft.Cdk.Constructs` (next minor after v2.5.0) |
| **AWS CDK floor** | `Amazon.CDK.Lib` 2.274.0 (raised from 2.273.0; see [§5.5](#55-cdk-version-floor)) |
| **Reference implementation** | Sharedale `infra/Sharedale.Infrastructure/SharedaleHostedStack.cs` (commit `b44b0a6`) |

> This page is a design record, not user documentation. It is not in the MkDocs `nav`; the user-facing
> page proposed in [§16](#16-documentation-strategy) is.

---

## 1. Executive summary

AWS Lambda Web Functions (launched 2026-10-12) run an ordinary HTTP server per execution environment behind
managed HTTPS endpoints. CloudFormation exposes them as `AWS::Lambda::WebFunction`, `WebFunctionRevision`,
`WebFunctionEndpoint`, and `AWS::Lambda::ResourcePolicy`. AWS CDK 2.274.0 has **only L1** support for them; two
competing upstream L2 proposals are open and unmerged.

Sharedale runs its Next.js BFF and its ASP.NET Core API as two Web Functions with `IamAuth` endpoints: CloudFront
reaches the BFF through Origin Access Control (OAC), and the BFF reaches the API by SigV4-signing with its execution
role. That works today, but it takes about 170 lines of hand-wired L1 code, an L1 OAC with literal strings, a
hand-built resource policy, and implicit ordering rules.

**Recommendation — hybrid composition (Approach C):**

- **`WebFunctionConstruct`**: a focused L3 that owns one Web Function's lifecycle — the function, its current
  immutable revision, its execution role and log group (each optional), one default endpoint plus any added with
  `AddEndpoint`, and the function's **single** resource-based policy.
- **`WebFunctionEndpointConstruct` / `IWebFunctionEndpoint`**: a first-class endpoint with `GrantInvoke(IGrantable)`,
  `GrantCloudFrontInvoke(distributionArn)`, and limited import support. `GrantInvoke` writes identity policies only to
  principals whose account it can establish. Anything it cannot fully configure (cross-account or unresolved
  imported principals) fails synthesis unless the consumer opts into `CallerPolicy = ExternallyManaged`
  ([§10.2](#102-grantinvoke-semantics)).
- **`WebFunctionOrigin`**: a static factory, analogous to CDK's `FunctionUrlOrigin`, that returns a CloudFront
  `IOrigin`. It configures OAC correctly (`lambda`, `always-amz-auth`, SigV4 or SigV4A chosen from the endpoint type)
  and, when the origin is bound to a `Distribution`, adds the `aws:SourceArn`-scoped statement for that distribution
  to the function's resource policy. A spike confirmed this works through the .NET jsii bindings ([§11.2](#112-spike-results)).
- Code comes from an **existing S3 object** (`WebFunctionCode.FromBucket`). Packaging and uploading stay with the
  consumer. `FromAsset` can be added later without breaking changes.
- **Workload-neutral.** The same API deploys Next.js, ASP.NET Core (Node.js `execve` shim), and Blazor Static SSR.
  The differences live in artifacts and configuration only. Recipes are documentation, not types.
- **Existing constructs are untouched.** Acceptance includes zero synthesis diff for `LambdaFunctionConstruct`,
  `StaticSiteConstruct`, `DynamoDbTableConstruct`, and `CognitoUserPoolConstruct`.
- **Release gate.** A successful sandbox deployment and smoke test is required before the feature is published
  ([§19.1](#191-release-acceptance-criteria)). It does not run in normal CI.

The construct stays thin over CloudFormation. It adds secure wiring, synth-time validation of invalid combinations,
and CDK-idiomatic grants. It does not add a deployment framework.

---

## 2. Goals and non-goals

### Goals

1. Deploy a Web Function from an S3 artifact with one construct and strongly typed configuration.
2. Make endpoint authentication an explicit decision on **every** endpoint. There is no default `AuthType`.
3. Grant `lambda:InvokeWebFunctionEndpoint` on a **specific endpoint** through CDK-conventional `GrantInvoke`.
4. Integrate an `IamAuth` endpoint with an existing CloudFront `Distribution` through OAC, including the required
   resource-policy permission, without taking ownership of the distribution, certificate, or DNS.
5. Own exactly one resource policy per Web Function. Every contribution flows through it.
6. Fail at synthesis for configurations that are insecure, nonfunctional, or misleading (for example, OAC on an
   `ApplicationManaged` endpoint).
7. Support Next.js, ASP.NET Core, and Blazor Static SSR through the same public API (an acceptance criterion).
8. Ship consumer test helpers that follow the repository's `Testing` namespace conventions.
9. Keep the public C# API independent of upstream L2 types. Adopting an upstream L2 later is then an optional
   internal refactor. It is permitted only if it also preserves deployment compatibility, which covers logical IDs,
   resource properties, dependencies, and replacement semantics ([§12.4](#124-compatibility-guarantees-and-future-upstream-l2-adoption)).

### Non-goals (v1)

- Building, publishing, zipping, or uploading application artifacts. No `BucketDeployment`, no custom resources.
- `WebFunctionCode.FromAsset` (designed for, not built).
- Weighted revisions, canary or blue-green routing, traffic shifting, or retaining previous revisions.
- Network connectors (VPC egress, `AWS::Lambda::NetworkConnector`).
- Whole-function imports (`FromWebFunctionAttributes`).
- Owning or creating CloudFront distributions, ACM certificates, Route 53 records, or WAF.
- Workload-specific constructs or CloudFront behavior helpers (Next.js, ASP.NET Core, Blazor).
- Caller-side request signing (SigV4 in the BFF) or injecting caller configuration (endpoint URL or region
  environment variables). These belong to the consumer's application.
- Alarms and dashboards. Metric helpers are a follow-up.
- Revision encryption with a customer managed key (`KmsKeyArn`). Deferred; see [§8.7](#87-kms-scope-decision). S3
  artifact encryption (SSE-S3/SSE-KMS on the code bucket) remains supported and documented.
- Maintained data lists (for example, opt-in Regions) and deep inspection of CloudFront distribution behaviors.
  Validation stays narrow ([§14](#14-validation-and-error-handling)).
- Any change to existing constructs.

---

## 3. Existing Sharedale implementation

Primary sources: `infra/Sharedale.Infrastructure/SharedaleHostedStack.cs`,
`SharedaleAccountBootstrapStack.cs`, `infra/web-functions/*.js`, `infra/scripts/build-web-functions.sh`,
`app/lib/server/api/web-function-iam.ts`, ADR-0009, ADR-0010, ADR-0011, `docs/architecture/hosted-environment.md`,
and `docs/architecture/lambda-web-functions-poc.md`.

### 3.1 Topology

```text
Browser ─HTTPS─> CloudFront (*.{domain}, ACM us-east-1, Route 53 A/AAAA aliases)
                   │ viewer-request CloudFront Function: X-Sharedale-Viewer-Host, cookie filter
                   │ origin custom header: X-Sharedale-Origin-Verification (Secrets Manager dynamic ref)
                   │ OAC: lambda / always-amz-auth / sigv4
                   │ origin request policy: ALL_VIEWER_EXCEPT_HOST_HEADER; default CACHING_DISABLED
                   │ /_next/static/* → CACHING_OPTIMIZED
                   ▼
  Web Function sharedale-{env}-web  (nodejs24.x, Next.js standalone; endpoint "default", HomeRegion, IamAuth)
                   │ resource policy: cloudfront.amazonaws.com, aws:SourceArn = this distribution
                   │ HTTPS, SigV4 (service "lambda", UNSIGNED-PAYLOAD) with the web execution role
                   ▼
  Web Function sharedale-{env}-api  (nodejs24.x → execve self-contained .NET 11 linux-arm64; IamAuth)
                     no resource policy; the web role's identity policy grants invoke on the endpoint ARN
```

There is no `/api/*` CloudFront behavior. The browser never calls the API.

### 3.2 Web Function definition (`SharedaleHostedStack.WebFunction`, lines 526–577)

| Concern | Sharedale | Generic or specific? |
|---|---|---|
| Resources | `CfnWebFunction` (name only), `CfnWebFunctionRevision`, `CfnWebFunctionEndpoint` | Generic |
| Ordering | `revision.AddResourceDependency(function)`, `endpoint.AddResourceDependency(revision)`; `FunctionName` is the **literal name** on all three | Generic (CFN needs it) |
| Runtime | `nodejs24.x` for both (the only supported runtime) | Generic |
| Code | `S3Object { Bucket = sharedale-artifacts-{account}, Key = {artifactTag}/{id}.zip }`, **no `VersionId`** | Bucket/key scheme is specific |
| Role | New `Role` per function, `AWSLambdaBasicExecutionRole`, statements added afterwards | Generic shape; statements specific |
| Logs | `LogGroup /aws/lambda/{name}`, one month, `DESTROY`; `TelemetryConfig.LoggingConfig.LogGroup` | Generic; name and retention are choices |
| Service config | `TimeoutSeconds = 30`, `MaxConcurrencyPerEnvironment = 16`, env vars | Values specific |
| Endpoint | `EndpointName = "default"`, `HomeRegion`, `IamAuth`, 100% to the revision, `MaxEnvironments = 10`, `RateLimit = 100` | Shape generic; values specific |
| Region | Stack throws unless `us-east-1` (because of the CloudFront certificate) | Specific |

### 3.3 Artifact bucket (`SharedaleAccountBootstrapStack.cs:113–167`)

The bucket is owned by a separate, long-lived stack: versioned, `S3_MANAGED`, `BLOCK_ALL`, `EnforceSSL`, `RETAIN`. Its
bucket policy grants `lambda.amazonaws.com` `s3:GetObject` and `s3:GetObjectVersion` with `aws:SourceAccount` and
`ArnLike aws:SourceArn` matching `web-function/sharedale-*` and `function:sharedale-*`. Uploads are denied unless
conditional (`s3:if-none-match`), and deletes are denied. Immutability comes from the bucket policy, not from a
pinned `VersionId`. Build: `infra/scripts/build-web-functions.sh`, publish: `publish-hosted-artifacts.sh`. Artifacts
must exist before CloudFormation creates the revisions.

**Implication for the library:** buckets that are imported or owned by another stack must be supported with an
*externally managed* bucket policy.

### 3.4 IAM-authenticated BFF → API

- Caller: the BFF execution role, through the Node.js default credential chain (`@aws-sdk/credential-provider-node`).
- Policy: an identity statement on the web role, `lambda:InvokeWebFunctionEndpoint` on
  `apiFunction.Endpoint.AttrEndpointArn` only (lines 247–251). There is no resource policy on the API, because the
  caller is in the same account.
- Signing: `app/lib/server/api/web-function-iam.ts`, SigV4 via `@smithy/signature-v4`, service `lambda`, region from
  `AWS_REGION`, `x-amz-content-sha256: UNSIGNED-PAYLOAD`. Enabled by `SHAREDALE_API_AUTH=iam`. The endpoint URL is
  wired as `SHAREDALE_API_URL = https://{Endpoint.AttrDomainName}`.
- The user's Cognito token travels in `X-Sharedale-User-Token`, never `Authorization`. Lambda strips the SigV4
  `Authorization` header before the request reaches ASP.NET Core (ADR-0010). Application authentication is
  independent of the AWS IAM boundary.
- Strictly server-to-server; no browser-side signing.

### 3.5 CloudFront and OAC (lines 327–418)

- OAC: `CfnOriginAccessControl` with `lambda`, `always-amz-auth`, `sigv4` (the L2 enums were missing in 2.273).
- Origin: `HttpOrigin(webEndpoint.AttrDomainName, HTTPS_ONLY, OriginAccessControlId, CustomHeaders{origin secret})`.
- Resource policy: `Amazon.CDK.AWS.Lambda.CfnResourcePolicy` on `webFunction.Function.AttrFunctionArn`, one statement
  for `cloudfront.amazonaws.com`, `lambda:InvokeWebFunctionEndpoint` on the endpoint ARN, `StringEquals
  aws:SourceArn = distribution.DistributionArn`. Same stack, so there is no cycle.
- Behaviors: default `CACHING_DISABLED` + `ALL_VIEWER_EXCEPT_HOST_HEADER` + viewer-request function; `/_next/static/*`
  `CACHING_OPTIMIZED`. TLS 1.2_2021, HTTP/2 and HTTP/3, `PRICE_CLASS_100`, wildcard ACM certificate, A and AAAA
  aliases for `app.` and `*.`.
- Host handling: the endpoint routes by `Host`, so the viewer host is moved into a custom header by a CloudFront
  Function and the BFF trusts it only after origin verification.
- Personalized responses are protected from caching because the default behavior uses `CACHING_DISABLED`. Only
  content-hashed assets are cached.
- Known one-time window: switching an endpoint to `IamAuth` before the distribution finishes deploying produces
  transient 403s (ADR-0011).

### 3.6 Generic vs. Sharedale-specific

**Generic (candidates for the library):** the function, revision, and endpoint triple with its dependencies; the
default role and log group; identity-policy invoke grants on an endpoint ARN; OAC configuration; the CloudFront
resource-policy statement scoped by `aws:SourceArn`; single-owner resource policy; HTTPS-only origin; the
host-header rule.

**Sharedale-specific (stay with the consumer):** the artifact bucket and its immutability policy; commit-addressed
keys; env var names; scaling values; the origin-verification secret header; the viewer-host CloudFront Function and
cookie filter; cache behaviors; the wildcard domain and DNS; Cognito wiring; alarms; the `us-east-1` restriction; the
SigV4 signer.

---

## 4. Existing LayeredCraft.Cdk.Constructs architecture

Source: `src/LayeredCraft.Cdk.Constructs/`, `test/LayeredCraft.Cdk.Constructs.Tests/`, `CLAUDE.md`, `docs/`.

| Area | Finding | Consequence for this design |
|---|---|---|
| Constructs | `LambdaFunctionConstruct` (classic Lambda, `PROVIDED_AL2023`, version + `live` alias, optional `FunctionUrlAuthType.NONE` URL), `StaticSiteConstruct` (public S3 website + CloudFront + ACM + `HostedZone.FromLookup`), `DynamoDbTableConstruct`, `CognitoUserPoolConstruct` | No CloudFront-generic or IAM-endpoint abstraction to extend. Web Functions are a different resource model from `AWS::Lambda::Function`. Extending `LambdaFunctionConstruct` would mix them (Approach A rejected). |
| Props convention | Interface + record. Older: mutable `{ get; set; }` + `required`. Newest (Cognito, #99): get-only interface, `init` record, injected `ICertificate?` / `IHostedZone?` instead of lookups | Follow the **newest** Cognito style |
| Naming | `*Construct` + `I*ConstructProps` / `*ConstructProps`; models in `Models/` namespace | `WebFunctionConstruct`, `IWebFunctionConstructProps`, … |
| Validation | `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, `NotSupportedException` (SnapStart) | Same exception style for synth-time validation |
| Exposed resources | Mixed: `public readonly Function LambdaFunction`, `UserPool UserPool { get; }` | Expose capability-oriented members (role, endpoint URL and ARN); L1s only via `Node.DefaultChild` and documented child IDs |
| Outputs | `LambdaFunctionConstruct`, `DynamoDbTableConstruct`, `CognitoUserPoolConstruct` emit `CfnOutput` exports via `StackExtensions.CreateExportName` | Do **not** emit outputs by default (decision: only the role and log group are owned infra) |
| Testing kit | Public `Testing` namespace: `CdkTestHelper`, `*ConstructAssertions`, `*ConstructPropsBuilder`; internal tests use AutoFixture `*AutoData` attributes and `[Collection("CDK Tests")]` | Add `WebFunctionConstructAssertions`, `WebFunctionConstructPropsBuilder`, `CdkTestHelper.CreateWebFunctionPropsBuilder()` |
| Targets | `net8.0;net9.0;net10.0;net11.0`; `Amazon.CDK.Lib` central version 2.273.0; CI `pr-build.yaml` with 80% coverage threshold | Bump to 2.274.0; keep TFMs |
| Docs | MkDocs Material, `docs/constructs/*.md`, `docs/examples/index.md`, `docs/testing/index.md`; non-strict build | New `docs/constructs/web-function.md` + nav entry |
| Versioning | Tags `v2.x.y`; release-drafter; `CHANGELOG.md` (last entry 2.0.0); `VersionPrefix` 2.4.0 in `Directory.Build.props` is stale versus tag v2.5.0 | Additive minor release. Ask the maintainer to reconcile `VersionPrefix` at release time (out of scope here). |

**Composition with existing constructs.** There is no CloudFront or DNS construct a Web Function could plug into
today. `StaticSiteConstruct` owns its distribution internally and exposes nothing. That confirms the decision:
`WebFunctionOrigin` composes with the CDK `Distribution` L2 the consumer creates, and certificates and DNS are plain
CDK (`Certificate`, `ARecord`/`AaaaRecord` + `CloudFrontTarget`), matching the Cognito construct's injected
`ICertificate` / `IHostedZone` style.

---

## 5. AWS / CDK capability findings

All claims below were verified on 2026-10-22 against the AWS documentation and the installed CDK packages.
References are in [§20](#20-references).

### 5.1 Resource model (CloudFormation)

| Resource | Key properties (update behavior) | Return values |
|---|---|---|
| `AWS::Lambda::WebFunction` | `FunctionName` (**Replacement**; 1–64 chars, `[A-Za-z0-9_-]`, cannot begin/end with `-`/`_`), `Tags` (no interruption) | `Ref` → ARN; `FunctionArn`, `State`, … |
| `AWS::Lambda::WebFunctionRevision` | **Every property → Replacement** (`BuildConfig{CodeConfig.S3Object{Bucket,Key,VersionId?}, RuntimeConfig.Runtime}`, `ServiceConfig{ExecutionRoleArn (req), EnvironmentVariables, TimeoutSeconds 3–900 (30), MaxConcurrencyPerEnvironment 1–128 (64), TelemetryConfig.LoggingConfig{LogGroup, ApplicationLogLevel, SystemLogLevel}, EgressNetworkConnectorArn}`, `KmsKeyArn`, `Description`, `FunctionName` name-or-ARN) | `RevisionId`, `RevisionArn`, … |
| `AWS::Lambda::WebFunctionEndpoint` | `AuthType` `IamAuth`/`ApplicationManaged` (**no interruption**), `EndpointName` (**Replacement**), `EndpointType` `HomeRegion`/`MultiRegion`/`PerRegion` (**Replacement**), `Regions` (**Replacement**; required for Multi/PerRegion, must include a non-home Region), `FunctionName` (**Replacement**), `RevisionWeights` 1–2 (no interruption), `ScalingConfig.MaxEnvironments` 2–10000, `ThrottleConfig.RateLimit` 0 or 100–1000 step 100 or 2000–10000 step 1000 (“can vary by Region”) | `DomainName`, `EndpointArn`, `RegionalEndpoints`, … |
| `AWS::Lambda::ResourcePolicy` | `ResourceArn` — function or **web-function ARN** (`…:web-function/{name}`, unqualified, no wildcards) (**Replacement**); `PolicyDocument` (no interruption). AWS warns against combining it with `AWS::Lambda::Permission` on the same function. | `Ref` → ResourceArn |

Runtime facts: `nodejs24.x` is the only runtime. The server must listen on `AWS_LAMBDA_HTTP_ENDPOINT` within 10 s.
The entry file defaults to `index.js`; `AWS_LAMBDA_NODEJS_ENTRYPOINT` overrides it. Each environment has 2 GB of
memory. HTTPS only on 443; HTTP/1.1; **no WebSocket upgrades**. Up to 50 revisions per function, with automatic
cleanup. The package is ≤ 512 MB unzipped. Endpoints scale to zero (cold starts). Default log group:
`/aws/lambda/web/{function-name}`. CloudWatch metric namespace observed in Sharedale: `AWS/Lambda/Web` (not
confirmed on the doc pages read).

Not confirmed by the AWS pages read (and **not** relied on by the library): arm64-only execution (asserted by
community samples), and the runtime-API readiness callback `POST /2025-09-01/ready` used by `execve` shims.

### 5.2 Code in S3

- S3 is the only code source. **The bucket must have versioning enabled.** `VersionId` is optional; if it is omitted,
  Lambda pins the latest version *at revision creation*.
- Lambda reads with the **service principal**, not the caller. The bucket policy grants `lambda.amazonaws.com`
  `s3:GetObject` and `s3:GetObjectVersion` with `ArnLike aws:SourceArn
  arn:aws:lambda:{region}:{account}:web-function/{name}/revision/*`.
- SSE-KMS **on the code bucket** (artifact encryption at rest in S3): the key policy must allow
  `lambda.amazonaws.com` `kms:Decrypt`, because Lambda reads the object with its service principal. The AWS-managed
  `aws/s3` key cannot grant that (upstream PRs reject `KMS_MANAGED`/`DSSE_MANAGED`). This is unrelated to the
  revision's own `KmsKeyArn` (§5.1), which encrypts the code and environment variables that Lambda stores for the
  revision, and for multi-Region endpoints requires a multi-Region key with replicas. v1 supports the first and
  defers the second ([§8.7](#87-kms-scope-decision)).
- Cross-account and cross-Region buckets are allowed (same partition, not an opt-in Region). Cross-account needs a
  bucket-policy grant.
- Deleting the pinned object version, or removing read access, moves revisions to a permanent `Failed` state, and
  requests get 409 `Platform.RevisionNotActive`.
- **The CDK bootstrap asset bucket does not work** (no service-principal read). Both upstream PRs work around this by
  copying into a compliant bucket with a custom resource. That is why v1 is `FromBucket` only.

### 5.3 IAM authentication

- Action `lambda:InvokeWebFunctionEndpoint` on `arn:aws:lambda:{r}:{a}:web-function/{fn}/endpoint/{ep}`;
  `…/endpoint/*` for all endpoints of a function.
- **Same account: an identity policy alone is sufficient.** Cross-account: identity policy **and** a resource-policy
  statement are both required.
- The resource policy is attached to the **function ARN**. There is one policy per function, and `put` replaces it.
  Statements scope `Resource` to an endpoint ARN.
- Signing: HomeRegion and PerRegion use SigV4 (endpoint Region). MultiRegion uses **SigV4A** with region set `*`.
  Service name `lambda`. Every request needs `X-Amz-Content-SHA256` (`UNSIGNED-PAYLOAD` allowed; chunked signing is
  not supported). Lambda strips the SigV4 `Authorization` header (and `X-Amz-Security-Token`) before forwarding.
- Condition keys: `lambda:Resource/WebFunctionAuthType`, `lambda:Request/WebFunctionAuthType`,
  `aws:ResourceTag/*` (endpoints and revisions inherit function tags), and `aws:SourceArn` (CloudFront).
- `ApplicationManaged`: Lambda does not authenticate or evaluate the resource policy. The endpoint “stays publicly
  reachable at its own domain, even behind CloudFront.”

### 5.4 CloudFront OAC for Web Function origins

- OAC origin type `lambda` (shared with Function URLs). **Only `always-amz-auth`** works for Web Function origins,
  and it works for no other origin type. CloudFront signs in a dedicated header and passes the viewer's
  `Authorization` header through unchanged.
- Protocol: `sigv4` for regional hostnames (`*.lambda-web.{region}.on.aws`, HomeRegion/PerRegion); `sigv4a` for
  global hostnames (`*.lambda-web.global.on.aws`, MultiRegion).
- The endpoint must be `IamAuth`. The resource-policy statement: `cloudfront.amazonaws.com`,
  `lambda:InvokeWebFunctionEndpoint`, endpoint ARN, `StringEquals aws:SourceArn
  arn:aws:cloudfront::{account}:distribution/{id}`.
- **The viewer `Host` header must not be forwarded**, or the endpoint returns 404. Use
  `AllViewerExceptHostHeader`, not `AllViewer`.
- For GET and HEAD, CloudFront drops `Authorization` unless it is in the **cache policy**.
- Bodies are signed as `UNSIGNED-PAYLOAD`, so viewers send nothing extra.
- **Lambda@Edge origin-request/origin-response triggers are rejected** on behaviors that use a Web Function origin
  with OAC.
- OAC is not supported for global endpoints that include **opt-in Regions** (403).
- OAC takes effect only after the distribution deploys (and, for multi-Region endpoints, after the policy
  replicates). Transient 403s occur in between.

### 5.5 CDK version floor

| Capability | 2.273.0 | 2.274.0 | Unreleased |
|---|---|---|---|
| Web Function L1s, `CfnResourcePolicy` | ✅ | ✅ | |
| `SigningBehavior.ALWAYS_AMZ_AUTH`, `SigningProtocol.SIGV4A` | ❌ | ✅ | |
| `Signing.SIGV4_ALWAYS_AMZ_AUTH` | ❌ | ✅ | |
| `Signing.SIGV4A_ALWAYS_AMZ_AUTH` static | ❌ | ❌ (use `new Signing(SIGV4A, ALWAYS_AMZ_AUTH)`) | aws-cdk#39216, merged 2026-10-22 |
| Web Function L2 | ❌ | ❌ | aws-cdk#39210, #39235 (open) |
| Web Function CloudFront origin | ❌ | ❌ | aws-cdk#39434 (open issue) |

Spike: on 2.274.0, `new FunctionUrlOriginAccessControl(..., Signing = new Signing(SigningProtocol.SIGV4A,
SigningBehavior.ALWAYS_AMZ_AUTH))` synthesizes `lambda` / `always-amz-auth` / `sigv4a`, and `Signing.SIGV4_ALWAYS_AMZ_AUTH`
synthesizes `sigv4`. **Decision: bump the floor to 2.274.0 and use the L2 `FunctionUrlOriginAccessControl`**
(origin type `lambda` is shared). There is no L1 OAC and no custom resource.

Observed on 2.273.0 during the spike: CDK's built-in template validation emits `WARNING Unknown resource type
'AWS::Lambda::WebFunction' (CloudFormation Validate, F3006)` for Web Function L1s. It is a warning by default and an
error with `@aws-cdk/core:validateAgainstDefaultRules`. Re-check on 2.274.0 during implementation and document it.

### 5.6 Native vs. gap vs. workaround

| Need | Native CDK | Library gap filled | Sharedale workaround to drop |
|---|---|---|---|
| Function/revision/endpoint | L1 only | Typed props, dependencies, validation | Hand-wired L1s |
| OAC | L2 `FunctionUrlOriginAccessControl` + `Signing` (2.274) | Correct signing per endpoint type | `CfnOriginAccessControl` with literals |
| Origin | `HttpOrigin` | `WebFunctionOrigin` factory + auto-grant at `Bind` | Manual `HttpOrigin` + manual policy |
| Resource policy | `CfnResourcePolicy` L1 | Single-owner, lazy, deduplicated | Manual `CfnResourcePolicy` |
| Grants | `Grant.AddToPrincipal`, `Grant.AddStatementToResourcePolicy` | `GrantInvoke` with explicit same/cross-account handling (§10.2) | Manual `PolicyStatement` |
| Code bucket policy | `IBucket.AddToResourcePolicy` | Least-privilege statement + dependency | Bootstrap-stack statement (kept, as *externally managed*) |

**No custom resources or escape hatches are required for v1.**

---

## 6. Architectural alternatives and tradeoffs

| | A — Extend existing | B — Dedicated full-stack | **C — Hybrid composition (chosen)** |
|---|---|---|---|
| Shape | Web Function modes on `LambdaFunctionConstruct`; OAC on `StaticSiteConstruct` | `WebApplicationConstruct` owning Web Function + CloudFront + ACM + DNS | `WebFunctionConstruct` + endpoint + `WebFunctionOrigin`; consumer owns Distribution, certificate, DNS |
| Evidence for | Reuses names | Least boilerplate for one simple site | Sharedale's edge (wildcard hosts, viewer-host function, cookie filter, secret header) is app-specific; the Cognito construct already moved to injected `ICertificate`/`IHostedZone`; CDK's `FunctionUrlOrigin` precedent |
| Evidence against | Different CFN resources (no versions/aliases/handler/memory/architecture; revisions instead); existing constructs are opinionated in incompatible ways (`PROVIDED_AL2023`, `live` alias, `Code.FromAsset`); regression risk; zero-diff requirement | Re-creates `StaticSiteConstruct`'s rigidity (zone lookup, fixed behaviors); can't represent Sharedale | More types than A |
| Breaking risk | High | None | None (additive) |

**Upstream relationship.** Build the L3 over L1s. Keep upstream L2 types out of the public API. Use upstream
*design ideas* where sound: required `authType` with no default; `grants.invoke` on the endpoint; synth-time
validation; owned-bucket checks; `node.defaultChild` set explicitly (their KMS service-grant pattern is noted for the deferred `EncryptionKey`).
Do **not** adopt either PR's API or package as a dependency or compatibility contract. Track aws-cdk#39434.

---

## 7. Recommended design and rationale

1. **One construct per Web Function** owning the resources whose lifecycle is the function's: the function, the
   current revision, endpoints, the resource policy, and *optionally* the role and log group.
2. **Endpoints are first-class** because grants, OAC, URLs, and auth are per endpoint. All endpoints route 100% to the
   construct's single current revision.
3. **Authorization is resource-oriented.** `endpoint.GrantInvoke(grantee)` and `endpoint.GrantCloudFrontInvoke(arn)`.
   The endpoint implements CDK's `IResourceWithPolicy` contract and composes CDK's `Grant` primitives, but it checks
   what was actually written instead of trusting `Grant.Success` (§10.2).
4. **CloudFront integration is an origin, not a distribution.** `WebFunctionOrigin.WithOriginAccessControl(endpoint)`
   mirrors `FunctionUrlOrigin.WithOriginAccessControl` and auto-grants at `Bind`.
5. **Fail closed and fail early.** Explicit `AuthType`, synth-time validation ([§14](#14-validation-and-error-handling)),
   an explicit check of every grant side the library is responsible for, no silent no-ops.
6. **Thin and honest.** Typed knobs exist only for AWS-defined capabilities. No workload presets. No derived
   environment variables except the AWS-defined entry point.
7. **Stable synthesis contract.** Construct child IDs, properties, and dependencies are pinned by full golden
   templates, separate from C# API stability ([§12.4](#124-compatibility-guarantees-and-future-upstream-l2-adoption)).

---

## 8. Proposed public API

Namespaces follow the repository: constructs and origin in `LayeredCraft.Cdk.Constructs`, props, options, enums, and
code types in `LayeredCraft.Cdk.Constructs.Models`, test helpers in `LayeredCraft.Cdk.Constructs.Testing`. Signatures
are normative for naming and shape. XML docs are abbreviated.

### 8.1 Enums and value types (`Models/`)

```csharp
public enum WebFunctionAuthType
{
    /// <summary>Lambda authenticates SigV4/SigV4A callers and authorizes them with IAM
    /// (identity policy and the function's resource policy). Required for OAC and GrantInvoke.</summary>
    IamAuth,
    /// <summary>Lambda performs no authentication; the endpoint is publicly reachable at its own domain,
    /// even behind CloudFront. The application must authenticate every request.</summary>
    ApplicationManaged,
}

public enum WebFunctionEndpointType { HomeRegion, MultiRegion, PerRegion }

/// <summary>Who maintains the lambda.amazonaws.com read statement on the code bucket.</summary>
public enum WebFunctionCodeBucketPolicy
{
    /// <summary>The construct adds a least-privilege statement. Requires a bucket the app can modify
    /// (an owned IBucket); an imported bucket fails synthesis.</summary>
    Automatic,
    /// <summary>The bucket policy is managed elsewhere (another stack, account, or process). Nothing is added.</summary>
    ExternallyManaged,
}

/// <summary>Who maintains the CloudFront statement in the endpoint's resource policy.</summary>
public enum WebFunctionOriginPermissions
{
    /// <summary>When the origin is bound to a distribution, add an aws:SourceArn-scoped statement for that
    /// distribution to the function's resource policy. Requires an endpoint owned by a WebFunctionConstruct
    /// in the distribution's stack.</summary>
    Automatic,
    /// <summary>The consumer guarantees that the function's resource policy already allows this distribution
    /// (for example, an imported endpoint, or an explicit GrantCloudFrontInvoke).</summary>
    ExternallyManaged,
}

public sealed class WebFunctionRuntime
{
    public static readonly WebFunctionRuntime NodeJs24X = new("nodejs24.x");
    /// <summary>Escape hatch for runtimes AWS adds before this package does.</summary>
    public static WebFunctionRuntime Of(string name);
    public string Name { get; }
}
```

Log levels reuse CDK's existing `Amazon.CDK.AWS.Lambda.ApplicationLogLevel` and `SystemLogLevel`. They are not
redefined.

### 8.2 Code

```csharp
public abstract class WebFunctionCode
{
    private protected WebFunctionCode() { }     // closed hierarchy: FromAsset can be added later

    public static WebFunctionCode FromBucket(IBucket bucket, string key, WebFunctionCodeFromBucketOptions? options = null);

    // internal: produces S3Object {Bucket, Key, VersionId?} and applies the bucket policy for `function`
    internal abstract WebFunctionCodeConfig Bind(WebFunctionConstruct function);
}

public sealed record WebFunctionCodeFromBucketOptions
{
    /// <summary>Pins the revision to this S3 object version. When null, Lambda pins the latest version
    /// at revision creation; later overwrites of the key do not affect existing revisions but do affect
    /// the next one CloudFormation creates.</summary>
    public string? ObjectVersion { get; init; }
    public WebFunctionCodeBucketPolicy BucketPolicy { get; init; } = WebFunctionCodeBucketPolicy.Automatic;
}
```

`Automatic` adds this statement to an owned bucket (`IBucket.AddToResourcePolicy(...).StatementAdded` must be
`true`, or synthesis fails telling the consumer to use `ExternallyManaged`):

```json
{ "Effect": "Allow", "Principal": { "Service": "lambda.amazonaws.com" },
  "Action": ["s3:GetObject", "s3:GetObjectVersion"],
  "Resource": "arn:{partition}:s3:::{bucket}/{key}",
  "Condition": {
    "StringEquals": { "aws:SourceAccount": "{account}" },
    "ArnLike": { "aws:SourceArn": "arn:{partition}:lambda:{region}:{account}:web-function/{FunctionName}/revision/*" } } }
```

The revision gets a dependency on the bucket policy (`PolicyDependable`). Region and account come from the
**function's** stack. If they are unresolved and the bucket is in a different stack, synthesis fails and asks for an
explicit stack `Env` (otherwise pseudo-parameters would resolve in the wrong stack).

### 8.3 `WebFunctionConstruct`

```csharp
public interface IWebFunctionConstructProps
{
    string FunctionName { get; }                                   // required; see §14
    WebFunctionCode Code { get; }                                  // required
    IWebFunctionEndpointOptions Endpoint { get; }                  // required: the default endpoint (AuthType required)
    WebFunctionRuntime Runtime { get; }                            // NodeJs24X
    string? EntryPoint { get; }                                    // → AWS_LAMBDA_NODEJS_ENTRYPOINT
    IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
    Duration Timeout { get; }                                      // 30 s; 3–900 s
    int? MaxConcurrencyPerEnvironment { get; }                     // AWS default 64; 1–128
    string? Description { get; }                                   // revision description
    IRole? Role { get; }                                           // null → created
    WebFunctionLogs Logs { get; }                                  // WebFunctionLogs.Create()
    ApplicationLogLevel? ApplicationLogLevel { get; }
    SystemLogLevel? SystemLogLevel { get; }
}

public sealed record WebFunctionConstructProps : IWebFunctionConstructProps
{
    public required string FunctionName { get; init; }
    public required WebFunctionCode Code { get; init; }
    public required IWebFunctionEndpointOptions Endpoint { get; init; }
    public WebFunctionRuntime Runtime { get; init; } = WebFunctionRuntime.NodeJs24X;
    public string? EntryPoint { get; init; }
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    public Duration Timeout { get; init; } = Duration.Seconds(30);
    public int? MaxConcurrencyPerEnvironment { get; init; }
    public string? Description { get; init; }
    public IRole? Role { get; init; }
    public WebFunctionLogs Logs { get; init; } = WebFunctionLogs.Create();
    public ApplicationLogLevel? ApplicationLogLevel { get; init; }
    public SystemLogLevel? SystemLogLevel { get; init; }
}

/// <summary>Where the revision's logs go. A closed choice, so "supplied group" and "create a group"
/// can never be combined or contradict each other.</summary>
public abstract class WebFunctionLogs
{
    private protected WebFunctionLogs() { }

    /// <summary>Create /aws/lambda/web/{FunctionName}. Defaults: RetentionDays.TWO_WEEKS, RemovalPolicy.DESTROY.</summary>
    public static WebFunctionLogs Create(RetentionDays? retention = null, RemovalPolicy? removalPolicy = null);

    /// <summary>Write to an existing log group, passed by name to the revision.</summary>
    public static WebFunctionLogs Use(ILogGroup logGroup);

    /// <summary>Create nothing and let Lambda use its default group (/aws/lambda/web/{FunctionName}),
    /// which has no expiry.</summary>
    public static WebFunctionLogs ServiceDefault();
}

public sealed class WebFunctionConstruct : Construct, IGrantable
{
    public WebFunctionConstruct(Construct scope, string id, IWebFunctionConstructProps props);

    public string FunctionName { get; }        // the literal name (or token) from props
    public string FunctionArn { get; }         // GetAtt FunctionArn
    public string RevisionId { get; }          // current revision
    public string RevisionArn { get; }
    public IRole Role { get; }                 // created or supplied execution role
    public IPrincipal GrantPrincipal { get; }  // Role.GrantPrincipal: lets table.GrantReadData(webFunction) etc.
                                               // (permissions OF this function, never invoke permissions ON it)
    public ILogGroup? LogGroup { get; }        // the created or supplied group; null for ServiceDefault()

    public WebFunctionEndpointConstruct DefaultEndpoint { get; }
    public IReadOnlyList<WebFunctionEndpointConstruct> Endpoints { get; }

    /// <summary>Adds another endpoint for the current revision. EndpointName must be unique within the function.</summary>
    public WebFunctionEndpointConstruct AddEndpoint(string id, IWebFunctionEndpointOptions options);
}
```

Resources created (stable child IDs, a public contract for `Node.FindChild` escape hatches):

| Child ID | Resource | When |
|---|---|---|
| `Function` (`Node.DefaultChild`) | `AWS::Lambda::WebFunction` | always |
| `Revision` | `AWS::Lambda::WebFunctionRevision` | always |
| `DefaultEndpoint` | `WebFunctionEndpointConstruct` → `Resource` = `AWS::Lambda::WebFunctionEndpoint` | always |
| *(consumer id)* | additional `WebFunctionEndpointConstruct` | `AddEndpoint` |
| `ExecutionRole` | `AWS::IAM::Role` + `AWSLambdaBasicExecutionRole` | `Role == null` |
| `LogGroup` | `AWS::Logs::LogGroup` named `/aws/lambda/web/{FunctionName}` | `Logs = WebFunctionLogs.Create(...)` (default) |
| `ResourcePolicy` | `AWS::Lambda::ResourcePolicy` (`ResourceArn` = function ARN) | first statement only |

Notes:

- The created log group's name equals AWS's default, so switching between `Create()` and `ServiceDefault()` never
  moves logs. Switching from `Create()` to `ServiceDefault()` deletes the CloudFormation-managed group under the
  default `DESTROY` policy; Lambda then recreates it without expiry. A group supplied with `Use(...)` is passed by
  name, and the role gets `logGroup.GrantWrite(role)` when the name is outside `/aws/lambda/*`.
- **Default retention is `RetentionDays.TWO_WEEKS`.** That matches the only existing retention convention in the
  library (`LambdaFunctionConstruct.cs:69`) and keeps a fresh deployment's log storage modest. It is a default, not a
  policy: Sharedale's 30 days is `WebFunctionLogs.Create(RetentionDays.ONE_MONTH)`. A Web Function-only default of 30
  days would make the library's two compute constructs disagree for no technical reason. **Decided (revision 3): `TWO_WEEKS`.**
- `FunctionName` is passed as the **literal name** to `Revision.FunctionName` and `Endpoint.FunctionName`, with
  explicit `AddResourceDependency` (function ← revision ← endpoints). This is CloudFormation's documented form and
  Sharedale's deployed shape. Switching to `Ref` (an ARN) later would *replace every endpoint*, so it is pinned.
- No revision KMS key in v1 ([§8.7](#87-kms-scope-decision)).
- Tags: `Tags.Of(construct)` reaches the taggable `CfnWebFunction`. Endpoints and revisions inherit function tags
  for ABAC.

### 8.4 Endpoints

```csharp
public interface IWebFunctionEndpointOptions
{
    string EndpointName { get; }                  // "default"; must be unique per function
    WebFunctionAuthType AuthType { get; }         // required, no default
    WebFunctionEndpointType EndpointType { get; } // HomeRegion
    IReadOnlyList<string> Regions { get; }        // Multi/PerRegion only
    int? MaxEnvironments { get; }                 // 2–10000; null = account vCPU quota only
    int? RateLimit { get; }                       // 0 | 100..1000 step 100 | 2000..10000 step 1000
    string? Description { get; }
}

public sealed record WebFunctionEndpointOptions : IWebFunctionEndpointOptions
{
    public string EndpointName { get; init; } = "default";
    public required WebFunctionAuthType AuthType { get; init; }
    public WebFunctionEndpointType EndpointType { get; init; } = WebFunctionEndpointType.HomeRegion;
    public IReadOnlyList<string> Regions { get; init; } = [];
    public int? MaxEnvironments { get; init; }
    public int? RateLimit { get; init; }
    public string? Description { get; init; }
}

public interface IWebFunctionEndpoint : IResource
{
    string EndpointName { get; }
    string EndpointArn { get; }
    string DomainName { get; }
    string Url { get; }                            // "https://" + DomainName
    WebFunctionAuthType AuthType { get; }
    WebFunctionEndpointType EndpointType { get; }
    Grant GrantInvoke(IGrantable grantee);
    Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options);
    void GrantCloudFrontInvoke(string distributionArn);
}

public sealed class WebFunctionEndpointConstruct : Resource, IWebFunctionEndpoint, IResourceWithPolicy
{
    // created only by WebFunctionConstruct (internal ctor)
    public WebFunctionConstruct WebFunction { get; }
    public IReadOnlyList<string> Regions { get; }

    /// <summary>Allows the grantee to call lambda:InvokeWebFunctionEndpoint on this endpoint only. Writes the
    /// policies this app can write and throws when a side it is responsible for cannot be written; see §10.2 for
    /// exactly which policies are modified and what a cross-account caller must configure itself.
    /// Throws for ApplicationManaged endpoints.</summary>
    public Grant GrantInvoke(IGrantable grantee);
    public Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options);   // §10.2

    /// <summary>Allows cloudfront.amazonaws.com to invoke this endpoint for one distribution
    /// (StringEquals aws:SourceArn). Accepts a token or a literal ARN; no Distribution construct required.
    /// Throws for ApplicationManaged and imported endpoints. Idempotent: identical statements are deduplicated.</summary>
    public void GrantCloudFrontInvoke(string distributionArn);

    /// <summary>IResourceWithPolicy: adds to the owning function's single resource policy. The statement's
    /// Resource must be exactly this endpoint's ARN (otherwise ArgumentException). This is the only public
    /// resource-policy entry point; WebFunctionConstruct has none of its own.</summary>
    public AddToResourcePolicyResult AddToResourcePolicy(PolicyStatement statement);

    public static IWebFunctionEndpoint FromEndpointAttributes(Construct scope, string id, WebFunctionEndpointAttributes attributes);
}

public sealed record WebFunctionEndpointAttributes
{
    public required string EndpointArn { get; init; }
    public required string DomainName { get; init; }
    public required WebFunctionAuthType AuthType { get; init; }
    public WebFunctionEndpointType EndpointType { get; init; } = WebFunctionEndpointType.HomeRegion;
}
```

Ownership semantics:

- `WebFunctionEndpointConstruct` instances are created only by `WebFunctionConstruct` (internal constructor). The
  function owns the revision, the endpoints, and the resource policy. An endpoint never creates resources of its own
  beyond its L1.
- An imported `IWebFunctionEndpoint` owns nothing. It can write identity policies on grantees whose account is
  established (§10.2 E1/E2) and nothing else. `GrantCloudFrontInvoke` and any grant that needs the resource policy throw ("the resource policy of an
  imported endpoint is externally owned"). Endpoint name, account, and Region are parsed from the ARN when it is
  literal. When the ARN is a token, the account is unresolved, so only owned (E1) grantees can be granted.
- The default endpoint and `AddEndpoint` endpoints have identical semantics. The only difference is that the default
  one is created from `props.Endpoint`. Leaving `EndpointName` at `"default"` in `AddEndpoint` fails as a duplicate.

### 8.5 CloudFront origin

```csharp
public static class WebFunctionOrigin
{
    /// <summary>An HTTPS origin for an IamAuth endpoint, signed by OAC (origin type lambda, always-amz-auth,
    /// sigv4 for HomeRegion/PerRegion, sigv4a for MultiRegion).</summary>
    public static IOrigin WithOriginAccessControl(IWebFunctionEndpoint endpoint, WebFunctionOriginWithOacProps? props = null);

    /// <summary>An HTTPS origin for an ApplicationManaged endpoint. The endpoint remains directly reachable
    /// on the internet; CloudFront adds no access control.</summary>
    public static IOrigin WithoutOriginAccessControl(IWebFunctionEndpoint endpoint, WebFunctionOriginProps? props = null);
}

/// <summary>The HttpOrigin settings a consumer may choose. Domain, protocol (HTTPS only), and the OAC id are
/// owned by the factory and deliberately not expressible here (revision 1 passed HttpOriginProps through
/// and rejected the conflicting members at runtime; that made ownership ambiguous).</summary>
public record WebFunctionOriginProps
{
    public string? OriginId { get; init; }
    public string? OriginPath { get; init; }
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; init; }
    public double? ConnectionAttempts { get; init; }
    public Duration? ConnectionTimeout { get; init; }
    public Duration? ReadTimeout { get; init; }
    public Duration? KeepaliveTimeout { get; init; }
    public Duration? ResponseCompletionTimeout { get; init; }
    public bool? OriginShieldEnabled { get; init; }
    public string? OriginShieldRegion { get; init; }
}

/// <summary>Mirrors CDK's FunctionUrlOriginWithOACProps extending FunctionUrlOriginProps.</summary>
public sealed record WebFunctionOriginWithOacProps : WebFunctionOriginProps
{
    /// <summary>An existing OAC (for example, one shared across distributions). When null, one
    /// FunctionUrlOriginAccessControl is created in the binding distribution's scope. A supplied OAC is used as is;
    /// the consumer is responsible for its configuration (lambda / always-amz-auth / protocol matching the endpoint).</summary>
    public IOriginAccessControl? OriginAccessControl { get; init; }
    public WebFunctionOriginPermissions Permissions { get; init; } = WebFunctionOriginPermissions.Automatic;
}
```

New CDK `HttpOriginProps` members are not exposed automatically. Each is added deliberately in a minor release. That
is a small maintenance cost, accepted in exchange for unambiguous ownership.

### 8.6 Testing helpers (`Testing/`)

```csharp
public static class WebFunctionConstructAssertions
{
    public static void ShouldHaveWebFunction(this Template t, string functionName);
    public static void ShouldHaveRevision(this Template t, string functionName, string runtime = "nodejs24.x");
    public static void ShouldHaveRevisionCode(this Template t, string bucketName, string key, string? versionId = null);
    public static void ShouldHaveEndpoint(this Template t, string functionName, string endpointName,
        WebFunctionAuthType authType, WebFunctionEndpointType type = WebFunctionEndpointType.HomeRegion);
    public static void ShouldHaveEndpointScaling(this Template t, string endpointName, int? maxEnvironments, int? rateLimit);
    // Matches by names (like the Cognito assertions), never by logical-ID prefixes:
    public static void ShouldHaveInvokeGrant(this Template t, string functionName, string endpointName);      // an identity statement on that endpoint's ARN
    public static void ShouldHaveSingleResourcePolicy(this Template t, string functionName);                  // exactly one, ResourceArn = that function
    public static void ShouldHaveNoResourcePolicy(this Template t);
    public static void ShouldAllowCloudFront(this Template t, string functionName, string endpointName);      // cloudfront.amazonaws.com + aws:SourceArn condition
    public static void ShouldAllowResourcePolicyPrincipal(this Template t, string functionName, string endpointName, string principalArn);
    public static void ShouldHaveWebFunctionOac(this Template t, string signingProtocol = "sigv4");
    public static void ShouldHaveCodeBucketReadStatement(this Template t, string functionName);
}

/// <summary>Follows the repository's builder convention (CognitoUserPoolConstructPropsBuilder,
/// StaticSiteConstructPropsBuilder): private fields, With*/Without* methods taking values or whole props
/// objects, Build() returning the init-only record. No mutation callbacks: the props records are init-only,
/// and callers customize an options record with a `with` expression before passing it in.</summary>
public class WebFunctionConstructPropsBuilder   // CdkTestHelper.CreateWebFunctionPropsBuilder()
{
    // Defaults: FunctionName "test-web-function"; no Code; no endpoint.
    public WebFunctionConstructPropsBuilder WithFunctionName(string functionName);
    public WebFunctionConstructPropsBuilder WithCode(WebFunctionCode code);
    /// <summary>Imports bucket "test-web-function-artifacts" in <paramref name="scope"/> and uses
    /// FromBucket(bucket, key, ExternallyManaged): synthesizes without AWS credentials or a bucket resource.</summary>
    public WebFunctionConstructPropsBuilder WithTestArtifact(Construct scope, string key = "artifacts/app.zip");
    public WebFunctionConstructPropsBuilder WithIamAuthEndpoint();                       // = WithEndpoint(new() { AuthType = IamAuth })
    public WebFunctionConstructPropsBuilder WithApplicationManagedEndpoint();            // = WithEndpoint(new() { AuthType = ApplicationManaged })
    public WebFunctionConstructPropsBuilder WithEndpoint(IWebFunctionEndpointOptions endpoint);
    public WebFunctionConstructPropsBuilder WithEnvironmentVariable(string key, string value);
    public WebFunctionConstructPropsBuilder WithEntryPoint(string entryPoint);
    public WebFunctionConstructPropsBuilder WithTimeout(Duration timeout);
    public WebFunctionConstructPropsBuilder WithMaxConcurrencyPerEnvironment(int maxConcurrency);
    public WebFunctionConstructPropsBuilder WithRole(IRole role);
    public WebFunctionConstructPropsBuilder WithLogs(WebFunctionLogs logs);
    /// <summary>Throws InvalidOperationException when Code or the endpoint (and therefore AuthType) was never
    /// chosen: the builder never picks an auth mode. Performs no other validation; the construct validates.</summary>
    public WebFunctionConstructProps Build();
}
```

Typical use:

```csharp
var props = CdkTestHelper.CreateWebFunctionPropsBuilder()
    .WithTestArtifact(stack)
    .WithEndpoint(new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth, MaxEnvironments = 10 })
    .Build();
var web = new WebFunctionConstruct(stack, "web", props);
Template.FromStack(stack).ShouldHaveEndpoint("test-web-function", "default", WebFunctionAuthType.IamAuth);
```

### 8.7 KMS scope decision

**Revision encryption (`KmsKeyArn`) is deferred from v1.**

| | S3 artifact encryption | Revision encryption |
|---|---|---|
| What it protects | The zip object at rest in the consumer's bucket | Code and environment variables that Lambda stores for the revision |
| Configured on | The bucket (SSE-S3 or SSE-KMS) | `AWS::Lambda::WebFunctionRevision.KmsKeyArn` (Replacement) |
| v1 | **Supported.** Owned bucket with `KMS_MANAGED`/`DSSE_MANAGED` rejected; SSE-KMS key-policy requirement (`lambda.amazonaws.com` `kms:Decrypt`) documented, consumer-managed | **Deferred** |

Why defer:

- The security model is not validated. The required key-policy grants for the service principal (which actions,
  whether `aws:SourceArn` on `…/revision/*` is evaluated for these calls) come only from upstream PR #39210's
  reasoning. They are not documented on the AWS pages read, and they are untested in a deployment.
- Multi- and Per-Region endpoints need a multi-Region key with replicas in every endpoint Region. That is a
  validation and documentation surface of its own.
- Imported keys cannot be granted, so the feature would ship with a warning path from day one.
- Without `KmsKeyArn`, Lambda applies its default encryption for revision data. Deferral does not remove encryption
  at rest; it removes customer-key control.

Escape hatch for advanced consumers (unsupported, documented as such):
`((CfnWebFunctionRevision)webFunction.Node.FindChild("Revision")).KmsKeyArn = key.KeyArn;` plus key-policy grants
they write themselves. A future minor release can add a typed `EncryptionKey` after a deployed validation, without
breaking changes.

---

## 9. Consumer usage examples

All examples assume a stack with an explicit `Env` and these usings: `Amazon.CDK`, `.AWS.CloudFront`,
`.AWS.CloudFront.Origins`, `.AWS.CertificateManager`, `.AWS.Route53`, `.AWS.Route53.Targets`, `.AWS.S3`,
`.AWS.DynamoDB`, `LayeredCraft.Cdk.Constructs`, `LayeredCraft.Cdk.Constructs.Models`.

### 9.1 ASP.NET Core API (Node.js `execve` shim)

```csharp
// Artifacts are published by CI to a versioned bucket owned by another stack (its policy grants
// lambda.amazonaws.com read on web-function/acme-*/revision/*).
var artifacts = Bucket.FromBucketName(this, "artifacts", $"acme-artifacts-{Account}");

var api = new WebFunctionConstruct(this, "api", new WebFunctionConstructProps
{
    FunctionName = "acme-prod-api",
    Code = WebFunctionCode.FromBucket(artifacts, $"{artifactTag}/api.zip", new WebFunctionCodeFromBucketOptions
    {
        BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged,
    }),
    // index.js at the zip root is the execve shim; no EntryPoint needed.
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["Acme__TableName"] = table.TableName,
    },
    MaxConcurrencyPerEnvironment = 16,
    Endpoint = new WebFunctionEndpointOptions
    {
        AuthType = WebFunctionAuthType.IamAuth,   // only granted callers reach Kestrel
        MaxEnvironments = 10,
        RateLimit = 100,
    },
});
table.GrantReadWriteData(api);                    // WebFunctionConstruct is IGrantable
```

### 9.2 Next.js application

```csharp
var web = new WebFunctionConstruct(this, "web", new WebFunctionConstructProps
{
    FunctionName = "acme-prod-web",
    Code = WebFunctionCode.FromBucket(codeBucket, $"{artifactTag}/web.zip", new WebFunctionCodeFromBucketOptions
    {
        ObjectVersion = webZipVersionId,          // optional: pin an exact object version
    }),                                           // codeBucket is owned in this stack → Automatic policy
    // The zip's index.js sets HOSTNAME/PORT from AWS_LAMBDA_HTTP_ENDPOINT, then require("./server.js").
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["NODE_ENV"] = "production",
        ["NEXT_TELEMETRY_DISABLED"] = "1",
        ["ACME_API_URL"] = api.DefaultEndpoint.Url,  // consumer-owned wiring
    },
    Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
});
```

### 9.3 Next.js granted permission to invoke the API

```csharp
// Identity policy on web's execution role: lambda:InvokeWebFunctionEndpoint on api's "default" endpoint ARN only.
// Same account → no resource-policy statement and no AWS::Lambda::ResourcePolicy on api.
api.DefaultEndpoint.GrantInvoke(web);

// The BFF signs requests itself (SigV4, service "lambda", region of the API endpoint,
// x-amz-content-sha256: UNSIGNED-PAYLOAD). The construct does not inject signing configuration.
```

Cross-account variant. It must be explicit, because `Automatic` fails at synthesis for a role in another account:

```csharp
var partner = Role.FromRoleArn(this, "partner", "arn:aws:iam::222233334444:role/caller");
api.DefaultEndpoint.GrantInvoke(partner, new WebFunctionInvokeGrantOptions
{
    CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
});
// Writes only api's resource-policy statement (principal = that role ARN, resource = the endpoint ARN) and adds an
// info annotation. Invocation works only after account 222233334444 attaches an identity policy allowing
// lambda:InvokeWebFunctionEndpoint on this endpoint ARN to its role.
```

### 9.4 CloudFront distribution with an IAM-protected Web Function origin (OAC)

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
        CachePolicy = CachePolicy.CACHING_DISABLED,                        // personalized SSR
        OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER, // never forward Host
    },
    AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
    {
        ["/_next/static/*"] = new BehaviorOptions { Origin = origin, CachePolicy = CachePolicy.CACHING_OPTIMIZED },
    },
    DomainNames = ["app.example.com"],
    Certificate = certificate,
});
// Bind created one FunctionUrlOriginAccessControl (lambda/always-amz-auth/sigv4) and added to web's resource policy:
//   cloudfront.amazonaws.com → lambda:InvokeWebFunctionEndpoint on web's default endpoint,
//   StringEquals aws:SourceArn = arn:aws:cloudfront::{account}:distribution/{distribution id}
```

### 9.5 Custom domain and Route 53

There is no library DNS construct to integrate with; composition is plain CDK. That matches
`CognitoUserPoolConstruct`'s injected `ICertificate`/`IHostedZone`, and the same certificate can be shared with a
Cognito custom domain:

```csharp
var zone = HostedZone.FromHostedZoneAttributes(this, "zone", new HostedZoneAttributes { HostedZoneId = zoneId, ZoneName = "example.com" });
var certificate = new Certificate(this, "cert", new CertificateProps   // CloudFront certificates live in us-east-1
{
    DomainName = "*.example.com",
    Validation = CertificateValidation.FromDns(zone),
});
// ... distribution from §9.4 with DomainNames = ["app.example.com"], Certificate = certificate ...
var target = RecordTarget.FromAlias(new CloudFrontTarget(distribution));
_ = new ARecord(this, "app-a", new ARecordProps { Zone = zone, RecordName = "app", Target = target });
_ = new AaaaRecord(this, "app-aaaa", new AaaaRecordProps { Zone = zone, RecordName = "app", Target = target }); // see #97

var auth = new CognitoUserPoolConstruct(this, "users", new CognitoUserPoolConstructProps
{
    UserPoolName = "acme",
    Domain = new CognitoUserPoolDomainProps { DomainName = "example.com", AuthSubDomain = "auth", Certificate = certificate, HostedZone = zone },
});
```

### 9.6 Web Functions without CloudFront or without inter-function IAM

```csharp
// (a) Public API, no CloudFront: the application authenticates every request.
var publicApi = new WebFunctionConstruct(this, "public-api", new WebFunctionConstructProps
{
    FunctionName = "acme-public-api",
    Code = WebFunctionCode.FromBucket(codeBucket, "public-api.zip"),
    Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.ApplicationManaged },
});
// publicApi.DefaultEndpoint.Url → https://….lambda-web.us-east-1.on.aws

// (b) IAM-only internal service invoked by a classic Lambda (no CloudFront):
var worker = new WebFunctionConstruct(this, "internal", new WebFunctionConstructProps
{
    FunctionName = "acme-internal",
    Code = WebFunctionCode.FromBucket(codeBucket, "internal.zip"),
    Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
});
worker.DefaultEndpoint.GrantInvoke(existingLambdaConstruct.LambdaFunction);

// (c) Two endpoints on one function: public for webhooks, IamAuth for CloudFront.
var hooks = web.AddEndpoint("webhooks", new WebFunctionEndpointOptions
{
    EndpointName = "webhooks",
    AuthType = WebFunctionAuthType.ApplicationManaged,
    RateLimit = 100,
});
```

### 9.7 Imported endpoint as a CloudFront origin (externally managed permissions)

```csharp
var shared = WebFunctionEndpointConstruct.FromEndpointAttributes(this, "shared", new WebFunctionEndpointAttributes
{
    EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
    DomainName = "abc123.lambda-web.us-east-1.on.aws",
    AuthType = WebFunctionAuthType.IamAuth,
});
var origin = WebFunctionOrigin.WithOriginAccessControl(shared, new WebFunctionOriginWithOacProps
{
    Permissions = WebFunctionOriginPermissions.ExternallyManaged,  // Automatic would fail synthesis
});
// The owner of "shared" must allow this distribution (aws:SourceArn) in the function's resource policy.
```

---

## 10. IAM authentication and authorization design

### 10.1 Layers

| Layer | Who decides | Mechanism |
|---|---|---|
| Endpoint authentication | Lambda | `IamAuth`: SigV4/SigV4A + `lambda:InvokeWebFunctionEndpoint` |
| Caller authorization | IAM | Identity policy (same account) and/or the function's resource policy |
| CloudFront-only access | IAM + OAC | Resource-policy statement with `aws:SourceArn` = distribution |
| User authentication/authorization | Application | Cookies, JWTs, etc. Never inferred from IAM. Lambda strips the SigV4 `Authorization`; with OAC the viewer `Authorization` passes through. |

The library configures only the first three layers. Documentation states plainly that `IamAuth` is a transport
boundary, not user authorization.

### 10.2 `GrantInvoke` semantics

**Verified CDK behavior (aws-cdk v2.274.0, `aws-iam/lib/grant.ts` 149–182, 369–379; `role.ts` 347–391).** Revision 1
proposed `Grant.AddToPrincipalOrResource` + `Grant.AssertSuccess()`. Reading the source shows that is not safe here:

- On the not-same-account path, `AddToPrincipalOrResource` attempts the principal statement and then **discards
  that result**. The returned `Grant` carries only the resource statement.
- `Grant.Success` is `principalStatement != null || resourceStatement != null`. That tests whether a statement
  **object** exists, not whether it was written. The resource statement object always exists on that path, so
  `AssertSuccess()` passes even when `AddToResourcePolicy` returned `StatementAdded = false`.
- `Role.FromRoleArn` returns a **mutable** imported role whenever either account is unresolved. CDK would then
  attach an `AWS::IAM::Policy` to that role name **in the deploying account**. If the role actually lives in another
  account, the deployment fails, because no such role exists there. CDK's `BOTH_UNRESOLVED ⇒ same account` rule
  makes the same assumption.

**Decision (revision 3): the library writes an identity policy only to a principal whose account it can
establish.** Otherwise it writes nothing on the identity side and requires an explicit contract.

```csharp
public enum WebFunctionCallerPolicy
{
    /// <summary>The library must fully configure invocation within this deployment, or synthesis fails.</summary>
    Automatic,
    /// <summary>The caller's identity policy is configured outside this app. The library writes only the
    /// endpoint's resource-policy statement.</summary>
    ExternallyManaged,
}

public sealed record WebFunctionInvokeGrantOptions
{
    public WebFunctionCallerPolicy CallerPolicy { get; init; } = WebFunctionCallerPolicy.Automatic;
}

// on IWebFunctionEndpoint / WebFunctionEndpointConstruct
Grant GrantInvoke(IGrantable grantee);                                         // = Automatic
Grant GrantInvoke(IGrantable grantee, WebFunctionInvokeGrantOptions options);
```

**Account establishment.** The grantee principal's account is *established as the endpoint's account* only when:

- **(E1) Owned principal:** the grant principal is a construct created in this app (`Resource.IsOwnedResource`, for
  example a `Role` L2, the role of a `Function`, or a `WebFunctionConstruct`). Its stack deploys its policy, and
  that stack's account must not be literally different from the endpoint stack's account. Both unresolved, or the
  same stack, is accepted, because the construct deploys where the role is created.
- **(E2) Imported principal with matching literal accounts:** the principal (for example `Role.FromRoleArn`) has a
  literal `PrincipalAccount` equal (`TokenComparison.SAME`) to a literal endpoint stack account.

Everything else is *not established*: imported principals with any unresolved account (`ONE_UNRESOLVED`, or
`BOTH_UNRESOLVED` from token ARNs or env-agnostic stacks), literal different accounts, and principals without
`PrincipalAccount`.

**Algorithm**

1. `AuthType != IamAuth` → throw.
2. **Identity side.** Only when the account is established: `Grant.AddToPrincipal(action
   lambda:InvokeWebFunctionEndpoint, resource = this endpoint ARN)`. A mutable imported same-account role gets an
   attached policy. An immutable one (`Mutable = false`) writes nothing; continue to step 3.
3. **Resource side.** Write `{Principal = grantee principal, Action, Resource = endpoint ARN}` through the function's
   single policy when:
   - the identity side was not written (an immutable same-account role, where a same-account resource-policy grant is
     sufficient per AWS); or
   - the principal is a **service principal** (complete without an identity policy); or
   - `CallerPolicy = ExternallyManaged`.

   An imported endpoint cannot write the resource side. If the resource side is required there, it throws.
4. **Completeness check under `Automatic`.** Synthesis fails unless invocation is fully configured by this app:
   identity side written (E1/E2), or immutable same-account role with resource side written, or service principal with
   resource side written. In particular these **fail**: an imported principal with an unresolved account, a
   different literal account, and account or ARN principals of other accounts. The message names the grantee, the
   endpoint, the reason ("account of imported role 'X' cannot be established"), and the two remedies: give the stacks
   explicit, matching `Env` accounts (or import the role with a literal ARN in the deployment account), or pass
   `CallerPolicy = ExternallyManaged` and configure the caller's identity policy in its own account.
5. **`ExternallyManaged`.** Never writes an identity policy, even when the account is established. It writes the
   resource-policy statement and adds an `Annotations` info: "Invocation by {principal} requires an identity policy in
   the caller's account allowing lambda:InvokeWebFunctionEndpoint on {endpoint ARN}." On an imported endpoint it
   throws, since there is no policy to write.
6. Return `Grant.Combine` of the statements actually written (`StatementAdded == true`). `Grant.Success` is never
   consulted.

| Grantee | Option | Identity policy written | Resource policy written | Consumer must configure | Result |
|---|---|---|---|---|---|
| Role/Function/WebFunction created in this app (E1) | Automatic | ✅ | — | nothing | **Complete** |
| `Role.FromRoleArn`, literal account = literal endpoint account (E2), mutable | Automatic | ✅ (role must exist) | — | nothing | **Complete** |
| Same as above, `Mutable = false` | Automatic | ❌ | ✅ | nothing | **Complete** (resource grant suffices same-account); imported endpoint → **throws** |
| Imported role, any unresolved account | Automatic | — | — | — | **Synthesis error** with remedies |
| Imported role, any unresolved account | ExternallyManaged | ❌ | ✅ | caller identity policy (in its account) | Partial by contract, annotated |
| `Role.FromRoleArn`, different literal account | Automatic | — | — | — | **Synthesis error** |
| `Role.FromRoleArn`, different literal account | ExternallyManaged | ❌ | ✅ | caller-account identity policy | Partial by contract, annotated |
| Service principal | Automatic | ❌ (n/a) | ✅ | nothing | **Complete** |
| `AccountPrincipal`/`ArnPrincipal` | Automatic | — | — | — | **Synthesis error** |
| `AccountPrincipal`/`ArnPrincipal` | ExternallyManaged | ❌ | ✅ | that account's identity policies | Partial by contract, annotated |
| Any grantee, owned endpoint | ExternallyManaged | ❌ | ✅ | caller identity policy | Partial by contract (useful to keep identity policies in the caller's stack/app) |

The library therefore never synthesizes an identity policy that could fail at deployment because the role is in
another account. It also never silently produces a half-configured grant: anything short of complete requires the
consumer to state `ExternallyManaged`.

### 10.3 Resource-policy ownership

- `WebFunctionConstruct` holds a private `PolicyDocument? _resourcePolicy`. The first contribution creates
  `CfnResourcePolicy("ResourcePolicy", ResourceArn = Function.AttrFunctionArn, PolicyDocument = _resourcePolicy)`.
  Later contributions call `_resourcePolicy.AddStatements(...)`. `PolicyDocument` resolves at synthesis, so statements
  added later (including during `Distribution` binding) are included and none overwrite earlier ones
  (spike-verified).
- Identical statements are deduplicated by `PolicyDocument` resolution (the spike added the same distribution grant
  twice and synthesized one statement).
- The only public entry points are on the endpoint: `AddToResourcePolicy` (whose `Resource` must be exactly that
  endpoint's ARN), `GrantInvoke`, `GrantCloudFrontInvoke`, and OAC binding. `WebFunctionConstruct` has no public
  `AddToResourcePolicy`, so every statement is endpoint-scoped by construction.
- Contract (documented): no other `AWS::Lambda::ResourcePolicy` or `AWS::Lambda::Permission` may target the
  function. An out-of-band `aws lambda-web put-resource-policy` is overwritten on the next deployment that changes
  the policy (drift). No detection machinery is added.

---

## 11. CloudFront OAC integration design

### 11.1 `Bind` behavior (`WithOriginAccessControl`)

Implemented as an internal `sealed class WebFunctionOacOrigin : DeputyBase, IOrigin`. Validation is limited to facts
the library knows with certainty (its own inputs and construct ownership). It does not inspect distribution
behaviors or maintain AWS data lists.

1. **Auth check (at factory call).** The endpoint must be `IamAuth`, or throw ("OAC cannot restrict an
   ApplicationManaged endpoint; it stays publicly reachable. Use IamAuth, or WebFunctionOrigin.WithoutOriginAccessControl
   to state that the endpoint is public.").
2. **Signing.** `HomeRegion`/`PerRegion` → `Signing.SIGV4_ALWAYS_AMZ_AUTH`; `MultiRegion` → `new
   Signing(SigningProtocol.SIGV4A, SigningBehavior.ALWAYS_AMZ_AUTH)`. Use `Signing.SIGV4A_ALWAYS_AMZ_AUTH` once
   released.
3. **OAC.** When none is supplied, create one `FunctionUrlOriginAccessControl` with child ID `WebFunctionOac` in the
   bind `scope` (the distribution's origin scope), as CDK does for `FunctionUrlOrigin`. The same origin object reused on
   several behaviors of one distribution binds **once** (spike-verified), so there is one OAC per distribution. A
   supplied `IOriginAccessControl` is used unchanged and not inspected (inspecting it would depend on CDK internals).
   Its correct configuration is a documented consumer responsibility.
4. **Permissions.** `Automatic`:
   - The endpoint is imported → throw ("cannot manage the resource policy of an imported endpoint; use
     Permissions = ExternallyManaged and grant the distribution in the owner's policy").
   - `Stack.Of(scope) != Stack.Of(endpoint)` → throw ("the distribution and the Web Function must be in the same
     stack for automatic permissions; otherwise CloudFormation has a dependency cycle (the distribution reads the
     endpoint domain, the policy reads the distribution id). Use ExternallyManaged and
     endpoint.GrantCloudFrontInvoke(<literal distribution ARN>)").
   - Otherwise `endpoint.GrantCloudFrontInvoke(Stack.Of(scope).FormatArn({ Service = "cloudfront", Region = "",
     Resource = "distribution", ResourceName = options.DistributionId }))`.

   `ExternallyManaged`: add nothing and add an info annotation naming the distribution that must be allowed.
5. **Return** `new HttpOrigin(endpoint.DomainName, { …WebFunctionOriginProps, ProtocolPolicy = HTTPS_ONLY,
   OriginAccessControlId = oac.OriginAccessControlId }).Bind(scope, options)`.

`WithoutOriginAccessControl` requires `ApplicationManaged` (an `IamAuth` endpoint without OAC answers 403 to every
request) and returns an HTTPS `HttpOrigin`.

**Removed in revision 2.** The opt-in Region list (stale data, and the AWS failure is a clear 403) and deep
`Distribution` inspection for `AllViewer`/Lambda@Edge (token resolution of `DistributionConfig` couples the library
to CDK internals). Both become documentation ([§11.3](#113-cache-and-header-guidance-documented-not-enforced)). The
optional T4a spike may test whether a **narrow** check (exact match of the managed `AllViewer` policy ID on
behaviors whose `Origin` object is this origin) can be done through public CDK APIs. It ships only if it needs no
template-resolution machinery, emits a warning (not an error), and has no false positives. Otherwise the check is
dropped. The design does not depend on it.

### 11.2 Spike results

The spike was disposable code under the session scratchpad, not committed (CDK 2.273.0 and 2.274.0, .NET 10, Node 26).

| Question | Result |
|---|---|
| Can a .NET class implement jsii `IOrigin` (via `DeputyBase`) and override `Bind`? | **Yes.** `Bind` is invoked once per distribution with `scope = {dist}/Origin1`. |
| Is `OriginBindOptions.DistributionId` available? | **Yes** (a token → `Ref` of the distribution). `Stack.FormatArn` produced `arn:${AWS::Partition}:cloudfront::{account}:distribution/${Ref}`. |
| Can `Bind` add resource-policy statements to a policy created earlier? | **Yes.** One `AWS::Lambda::ResourcePolicy`; two distributions → two statements; a repeated explicit grant was deduplicated. |
| Same origin object on two behaviors of one distribution? | Bound **once**; one origin entry. |
| Same stack: dependency cycle? | **No.** Policy → endpoint ARN + distribution `Ref`; distribution → endpoint `DomainName`. |
| Distribution in another stack? | **`DependencyCycle: 'fn' depends on 'dist' … (dist -> fn/web/Endpoint.DomainName) would create a cyclic reference`** → confirms §11.1 step 6 fail-fast. |
| L2 OAC for Web Functions on 2.274? | `FunctionUrlOriginAccessControl` + `Signing` → `lambda`/`always-amz-auth`/`sigv4` or `sigv4a`. |

### 11.3 Cache and header guidance (documented, not enforced)

- Dynamic or personalized routes: `CACHING_DISABLED` + `ALL_VIEWER_EXCEPT_HOST_HEADER`. Never `AllViewer`.
- Apps that read the viewer `Authorization` header on GET/HEAD must add it to a **cache policy** (CloudFront drops it
  otherwise). Prefer cookies or custom headers, as Sharedale does.
- Host-dependent apps (multi-tenant hosts, Better Auth origins) must recover the viewer host through a CloudFront
  Function that copies it to a custom header (Sharedale's `ViewerRequestFunctionCode` pattern) and trust it only
  after an origin-verification check.
- Content-hashed static assets (`/_next/static/*`, Blazor `/_framework/*` fingerprinted files, `/_content/*`):
  `CACHING_OPTIMIZED`.

---

## 12. Deployment and resource dependency considerations

### 12.1 Ordering

`Bucket policy (Automatic) → Revision`, `Function → Revision → each Endpoint`, `Endpoint → Distribution (origin
domain)`, `Endpoint + Distribution → ResourcePolicy`, `Role/LogGroup → Revision`. The S3 object (and the externally
managed bucket policy) **must exist before the stack deploys**. The construct cannot verify that at synthesis.

### 12.2 Updates, replacement, rollback

| Change | Effect | Guidance |
|---|---|---|
| Code key/version, env vars, timeout, role, logging, description | New revision (Replacement). Endpoints move to it (no interruption). The old revision is deleted in cleanup. | Normal deploy (~1–2 min in Sharedale). |
| Failed deploy | CloudFormation rolls back endpoint weights to the previous revision, which still exists until cleanup. | — |
| Roll back after success | No re-selection of old revisions. Redeploy the earlier artifact (a new revision). | Matches Sharedale's runbook. |
| `AuthType` change | No interruption. | `ApplicationManaged → IamAuth` with a new OAC causes transient 403s until the distribution deploys (ADR-0011). |
| `MaxEnvironments`, `RateLimit`, `Description` | No interruption. | CloudFormation treats an omitted config as "keep" and `{}` as "clear", so removing a value may need an explicit `{}` (open question 4). |
| `EndpointName`, `EndpointType`, `Regions` | **Replacement** with a fixed name → CloudFormation fails ("already exists"), and the domain changes. | Documented. To change, add a new endpoint with a new name, move consumers, then remove the old one. |
| `FunctionName` | **Replacement** of everything. Fails while the old name exists; endpoint domains change. | Documented as a breaking infrastructure change. |
| Distribution replacement | New ID → policy statement updated in the same deployment (no interruption). The new distribution may 403 until the policy update and propagation finish. | Documented. |
| Policy statement churn | `PolicyDocument` update, no interruption. | — |

### 12.3 Cross-stack

- The Web Function and its caller in different stacks: `GrantInvoke` adds an identity policy in the caller's stack
  referencing the endpoint ARN (caller → function). OK.
- The Distribution in a different stack: automatic OAC permissions fail fast ([§11.1](#111-bind-behavior-withoriginaccesscontrol)).
  Use `ExternallyManaged` + `GrantCloudFrontInvoke(literal ARN)`, which needs a two-phase rollout, or keep them in one
  stack (recommended, like Sharedale).
- An owned code bucket in a different stack: `Automatic` adds the statement to the bucket's stack (bucket stack →
  literal names only). OK when the function's stack `Env` is resolved.

### 12.4 Compatibility guarantees and future upstream L2 adoption

The library makes two separate guarantees.

**(1) C# public API stability (semver).** Within a major version, public type names, members, signatures, defaults,
and documented behavior do not change incompatibly. Upstream L2 types never appear in the public API, so adopting an
upstream L2 cannot break consumer code.

**(2) Deployment compatibility.** Upgrading the package within a major version, with unchanged consumer code and
props, must not produce a CloudFormation change set that replaces, removes, or materially changes a deployed
resource, unless the change is a documented fix in `CHANGELOG.md`. Logical-ID preservation is **necessary but not
sufficient**. Compatibility means equivalence of:

- logical IDs and resource types;
- **all** resource properties, not only replacement-triggering ones, because any change to a
  `WebFunctionRevision` property creates a new revision and shifts traffic;
- the literal `FunctionName` form on the revision and endpoints (name, not `Ref`/ARN, since endpoint `FunctionName`
  is replacement-triggering);
- `DependsOn` edges and implicit reference dependencies (function → revision → endpoints; bucket policy →
  revision; endpoint and distribution → resource policy), which govern create, update, and rollback order;
- `DeletionPolicy`/`UpdateReplacePolicy` (e.g. log group `DESTROY`);
- the content of IAM identity statements, the resource policy, and the code-bucket statement (permission changes are
  behavior changes);
- outputs and exports (none in v1).

Consequences of breaking (2):

| Resource | Replacement consequence |
|---|---|
| WebFunction (named) | **Deployment fails** (name conflict) or outage |
| Endpoint (named) | **Deployment fails**; domain changes; CloudFront origin breaks |
| ExecutionRole | New role ARN; revision replaced |
| LogGroup (named) | **Deployment fails** (name conflict) |
| ResourcePolicy | Window without CloudFront or cross-account permission (403) |
| Revision | New revision and traffic shift. Expected for consumer changes, not acceptable as a side effect of a package upgrade |

**Enforcement.** A golden-template test synthesizes representative stacks (single IamAuth function; two endpoints
with OAC on two distributions; cross-account grant; `ExternallyManaged` code and permissions; `Use(logGroup)`) and
compares the **complete** templates, normalized only for asset hashes and CDK metadata, against checked-in files. Any
diff fails CI, and an intentional diff requires updating the golden file and a CHANGELOG entry in the same PR.

**Upstream L2 adoption** is optional and never required of consumers. It is permitted only if the golden templates
are unchanged, which in practice means: nest the L2 under the existing construct, `OverrideLogicalId` every L1 it
creates, keep passing the literal function name, disable any upstream-created role, log group, or code bucket that
duplicates ours, keep resource-policy ownership in this construct, and reproduce the dependency edges. If equivalence
cannot be reached, the library does not adopt the L2 in that major version. An eventual adoption that changes
deployments is a **major** version with a documented migration (for example, `OverrideLogicalId` guidance or a
planned recreate).

### 12.5 Upgrade/rollback of the package itself

Additive. Existing constructs are untouched (their golden templates are asserted). Downgrading the package after
adopting the new constructs removes the types, so consumer code would not compile. Deployed infrastructure is
unaffected until the consumer changes it.

---

## 13. Backward compatibility and migration

- **Semver:** minor release (2.6.0 if released next). New public types only, so guarantee (1) holds trivially for
  existing APIs. The CDK floor rises to 2.274.0, a dependency-minimum bump called out in `CHANGELOG.md`.
  Consumers on < 2.274 get a NuGet resolution upgrade.
- **Existing constructs:** zero code changes. Guarantee (2) is enforced by golden templates of all four constructs
  (default and representative configurations), added in the CDK-bump PR before any new code, and kept as regression
  tests.
- **Sharedale:** not migrated (issue #98 constraint). For the record, a future Sharedale migration would change
  construct paths and therefore logical IDs of named resources (function, endpoint, log group), and would change the
  log group default. It would need `OverrideLogicalId` and `WebFunctionLogs.Use(...)`, or a planned recreate.
  Its bucket policy maps to `ExternallyManaged`.

---

## 14. Validation and error handling

Errors are thrown at construction or bind time (synthesis) with `ArgumentException`/`ArgumentOutOfRangeException`/
`InvalidOperationException`, following repository style. Literal values are validated; tokens are skipped
(`Token.IsUnresolved`), never rejected. Scope rule: validate the library's **own inputs and ownership facts**. Do not
validate AWS facts that change over time (Region lists, per-Region quotas) or the contents of constructs the library
does not own (distribution behaviors, supplied OACs, imported buckets).

| Rule | Where | Failure |
|---|---|---|
| `FunctionName` 1–64, `^[A-Za-z0-9](?:[A-Za-z0-9_-]*[A-Za-z0-9])?$` | ctor | `ArgumentException` |
| `EndpointName` 1–64, `[A-Za-z0-9_-]`; unique per function | ctor/AddEndpoint | `ArgumentException` / duplicate `InvalidOperationException` |
| `Timeout` 3–900 s, whole seconds | ctor | `ArgumentOutOfRangeException` |
| `MaxConcurrencyPerEnvironment` 1–128 | ctor | `ArgumentOutOfRangeException` |
| `MaxEnvironments` 2–10000 | endpoint | `ArgumentOutOfRangeException` |
| `RateLimit` ∈ {0} ∪ {100..1000 step 100} ∪ {2000..10000 step 1000} (documented grid; Regional variance left to AWS) | endpoint | `ArgumentOutOfRangeException` |
| `Regions`: empty for HomeRegion (or only the home Region); ≥ 1 non-home Region for Multi/PerRegion; ≤ 50; no duplicates | endpoint | `ArgumentException` |
| Env var keys `^[A-Za-z][A-Za-z0-9_]*$`; `EntryPoint` and `AWS_LAMBDA_NODEJS_ENTRYPOINT` not both set; `AWS_LAMBDA_HTTP_ENDPOINT` reserved | ctor | `ArgumentException` |
| `EntryPoint` relative, no leading `/`, no `..` | ctor | `ArgumentException` |
| Code: key non-empty; `Automatic` when `AddToResourcePolicy` reports `StatementAdded = false` (imported bucket) | Code.Bind | `InvalidOperationException` |
| Code: owned `Bucket` (L2 created in this app) without versioning, or with `KMS_MANAGED`/`DSSE_MANAGED` | Code.Bind | `InvalidOperationException` (owned buckets only; imported buckets are not inspected) |
| Code: owned bucket in another stack while the function stack's account or Region is unresolved | Code.Bind | `InvalidOperationException` |
| `GrantInvoke` / `GrantCloudFrontInvoke` on `ApplicationManaged` | endpoint | `InvalidOperationException` |
| `GrantCloudFrontInvoke`, or a grant needing the resource policy, on an imported endpoint | endpoint | `InvalidOperationException` |
| `Automatic` grant not fully configurable (imported principal with unresolved account, different literal account, account/ARN principals) | endpoint | `InvalidOperationException` naming grantee, endpoint, reason, and remedies (§10.2 step 4) |
| `ExternallyManaged` grant on an imported endpoint | endpoint | `InvalidOperationException` |
| `AddToResourcePolicy` whose `Resource` is not exactly this endpoint's ARN | endpoint | `ArgumentException` |
| OAC origin for `ApplicationManaged`; non-OAC origin for `IamAuth` | origin factory | `InvalidOperationException` |
| Automatic OAC permissions with an imported endpoint or a cross-stack distribution | Bind | `InvalidOperationException` |
| Props builder `Build()` without code or endpoint | Testing | `InvalidOperationException` |

Documented, not validated: forwarding the viewer `Host` (`AllViewer`) → 404; Lambda@Edge origin triggers → rejected by
CloudFront; OAC with opt-in Regions on global endpoints → 403; a supplied OAC's configuration; imported buckets'
versioning, encryption, and policy; S3 object existence.

---

## 15. Testing strategy

All tests go in `test/LayeredCraft.Cdk.Constructs.Tests/`, `[Collection("CDK Tests")]`, xUnit v3, AwesomeAssertions,
`Template.FromStack`, and AutoFixture `WebFunctionConstructAutoData` with customization classes following the
existing TestKit. The 80% coverage gate applies.

**Synthesis/template**

- Function, revision, and default endpoint exist with literal `FunctionName` on all three; `DependsOn` chain; runtime
  `nodejs24.x`; `S3Object` with/without `VersionId`; env vars incl. `AWS_LAMBDA_NODEJS_ENTRYPOINT`; timeout,
  concurrency, logging levels; **no** `KmsKeyArn`.
- Defaults: created role with `AWSLambdaBasicExecutionRole`. `WebFunctionLogs.Create()` → `/aws/lambda/web/{name}`,
  14 days, `Delete`. `Create(ONE_MONTH)` → 30. `ServiceDefault()` → no log group and no `LoggingConfig.LogGroup`.
  `Use(group)` → that name, no group created, `GrantWrite` only outside `/aws/lambda/*`. A supplied role is used and
  none is created.
- Endpoint: name, type, `Regions`, `AuthType` per endpoint, scaling/throttle present/absent, revision weight 100 to
  the current revision on **every** endpoint.
- Multiple endpoints with different auth types; duplicate names (including a second `"default"`) rejected.

**IAM (one test per row of the §10.2 table)**

- E1 owned role, Function, or WebFunctionConstruct grantee → identity statement on the specific endpoint ARN only
  (not `endpoint/*`, not other endpoints); **no** `AWS::Lambda::ResourcePolicy`. Also covers env-agnostic stacks
  (both accounts unresolved).
- E2 `Role.FromRoleArn` with literal account equal to the literal stack account → policy attached by name; no
  resource policy.
- E2 with `Mutable = false` → resource statement only; imported endpoint throws.
- Imported role with a token ARN, or a literal ARN in an env-agnostic stack, under `Automatic` → throws, and **no**
  `AWS::IAM::Policy` is synthesized. Under `ExternallyManaged` → resource statement + annotation, no identity policy.
- Different literal account: `Automatic` throws; `ExternallyManaged` → resource statement only + annotation; the
  returned `Grant` has no principal statements.
- Service principal → resource only (complete). `AccountPrincipal`/`ArnPrincipal`: `Automatic` throws;
  `ExternallyManaged` → resource only.
- `ExternallyManaged` with an E1 grantee → resource statement only, no identity policy.
- `ApplicationManaged` → throws; `ExternallyManaged` on an imported endpoint → throws.
- Regression test pinning the CDK pitfall: a fake `IResourceWithPolicy` returning `StatementAdded = false` makes
  `GrantInvoke` throw (it must not rely on `Grant.Success`).

**Resource-policy ownership**

- No statements → no resource; many grants → exactly one; multiple distributions → all statements; different
  orders → equal statement *sets*; repeated grants → no duplicates; foreign `Resource` → throws; `WebFunctionConstruct`
  exposes no `AddToResourcePolicy` (a compile-time API surface test).

**CloudFront/OAC**

- OAC `lambda`/`always-amz-auth`/`sigv4` (HomeRegion, PerRegion) and `sigv4a` (MultiRegion); the origin uses the
  endpoint `DomainName`, `https-only`, `OriginAccessControlId`; `WebFunctionOriginProps` members pass through.
- Auto statement `aws:SourceArn` = distribution ARN; same origin on two behaviors → one statement; two
  distributions → two statements, each scoped; a second endpoint's origin → statement scoped to that endpoint ARN.
- Supplied OAC reused, none created; `ExternallyManaged` → no statement + annotation; explicit
  `GrantCloudFrontInvoke(literal)` works with no Distribution construct.
- Negatives: `ApplicationManaged` + OAC; `IamAuth` + `WithoutOriginAccessControl`; imported + Automatic; cross-stack +
  Automatic (library error message, not a CDK cycle).

**Code bucket**

- `Automatic` on an owned bucket → least-privilege statement (`/revision/*`, `aws:SourceAccount`) + revision
  dependency; imported + `Automatic` → error; `ExternallyManaged` → no statement; unversioned or `KMS_MANAGED` owned
  bucket → error; cross-stack owned bucket with unresolved function env → error.

**Validation negatives:** each row of §14.

**Compatibility and regression:** the golden templates of §12.4 for the new construct, and of the four existing
constructs (zero-diff acceptance).

**Workloads (acceptance criterion):** compiled consumer-example tests reproducing §9.1–9.7, plus one synthesis test
per workload recipe (Next.js, ASP.NET Core shim, Blazor Static SSR shim). Each must use only the public API, with
differences expressed only through `Code`, `EntryPoint`, `EnvironmentVariables`, and the consumer's CloudFront
behaviors.

**Consumer helpers:** tests for `WebFunctionConstructAssertions` (including multi-endpoint discrimination and
single-resource-policy checks) and for `WebFunctionConstructPropsBuilder` (`Build()` throws without an auth choice
or code; `WithTestArtifact` synthesizes without credentials).

**Deployed validation:** a **release gate**, not CI; see [§19.1](#191-release-acceptance-criteria).

---

## 16. Documentation strategy

- **New `docs/constructs/web-function.md`** (added to `nav` under Constructs): overview; auth modes and security
  implications (a table: who can reach the endpoint); `WebFunctionCode.FromBucket` requirements (versioning,
  `s3:GetObject`/`s3:GetObjectVersion`, the `/revision/*` SourceArn statement, optional `VersionId` pinning
  semantics, cross-account buckets, SSE-KMS key policy, bootstrap bucket incompatibility, the object must exist
  before deploy); endpoints and `AddEndpoint`; behavior on revision change; `GrantInvoke` (same- vs cross-account,
  the caller signs SigV4 with `UNSIGNED-PAYLOAD`, strip semantics); resource-policy single-owner contract; OAC
  origin; cache/header guidance; replacement table; validation errors; limitations.
- **Workload recipes** (in the same page or `docs/examples/web-functions.md`):
  1. *Next.js*: `output: "standalone"`; zip root = standalone output + `.next/static` + `public`; `index.js` that sets
     `HOSTNAME`/`PORT` from `AWS_LAMBDA_HTTP_ENDPOINT`, adjusts `NODE_PATH` for pnpm, handles SIGTERM, and requires
     `server.js` (Sharedale `infra/web-functions/next-index.js`); `NODE_ENV=production`; OAC; `/_next/static/*`
     cached, everything else uncached + `AllViewerExceptHostHeader`; host recovery via a CloudFront Function if
     host-dependent; ISR/image optimization caveats (no shared cache across environments, ephemeral FS).
  2. *ASP.NET Core API*: self-contained `linux-arm64` publish (`server` apphost) + `index.js` shim
     `process.execve("/var/task/server", …)` (Sharedale `infra/web-functions/dotnet-index.js`); Kestrel on
     `http://{AWS_LAMBDA_HTTP_ENDPOINT}`; readiness `POST /2025-09-01/ready` to `AWS_LAMBDA_RUNTIME_API` after start
     (e.g. `LayeredCraft.LambdaWebFunctions.Hosting`); app-local ICU (`Microsoft.ICU.ICU4C.Runtime`) or invariant
     globalization (the runtime has no `libicu`); no `UseHttpsRedirection`/HSTS (TLS terminates at the endpoint);
     `IamAuth` + `GrantInvoke` from callers; usually no CloudFront. Flagged: `execve` and the readiness path are not
     documented by AWS.
  3. *Blazor Static SSR* (per `j-d-ha/lambda-web-function-not-node` `src/blazor`): same shim and readiness contract as
     ASP.NET Core; untrimmed server publish (reflection-based rendering); `ContentRootPath = AppContext.BaseDirectory`
     when `AWS_LAMBDA_HTTP_ENDPOINT` is set so `wwwroot`/`MapStaticAssets` resolve; zip contains `wwwroot/_framework`
     (~57 MB zipped / ~136 MB unzipped measured, under the 512 MB limit); optional Interactive WebAssembly components
     (plain HTTP); cache fingerprinted `/_framework/*` and `/_content/*`, uncached SSR pages, `AllViewerExceptHostHeader`.
     **Blazor Interactive Server / Auto / legacy Blazor Server are unsupported**: they need a SignalR WebSocket (Web
     Functions reject upgrades) and session affinity, and long polling does not help on a half-duplex,
     timeout-bounded request path.
- `README.md` construct list; `docs/index.md`; `docs/testing/index.md` (new assertions/builder);
  `docs/examples/index.md` (§9 examples); `CHANGELOG.md` (new types, CDK floor 2.274.0); repository `CLAUDE.md`
  architecture section.
- Security guidance callouts: `ApplicationManaged` is public even behind CloudFront; `IamAuth` ≠ user auth; never
  forward `Host`; Lambda@Edge origin triggers are incompatible; same-stack requirement for automatic OAC permissions.

Documented rather than abstracted: artifact build and upload; `execve` shim; readiness; ICU; viewer-host recovery;
origin-verification secrets and their rotation (Sharedale runbook); cold starts (~3–5 s measured by Sharedale);
regional `RateLimit` variance; deployment-time 403 windows.

---

## 17. Risks and limitations

| Risk | Mitigation |
|---|---|
| Upstream L2 lands with different semantics, and consumers expect parity | Public API independent of upstream; adoption gated by full golden-template equivalence (§12.4) |
| jsii override behavior changes across CDK versions | Spike-proven on 2.273/2.274; origin tests run in CI on every CDK bump |
| CDK `Grant` semantics change | `GrantInvoke` checks `StatementAdded` itself; a regression test pins the behavior (§15) |
| Consumers assume `GrantInvoke` configured a cross-account caller | §10.2 table, annotation, XML docs, and user docs state the caller-side requirement |
| Misconfigured CloudFront behaviors (Host forwarded, Lambda@Edge) are not caught at synthesis | Documented; the deployed smoke test exercises the documented configuration; AWS fails loudly (404 / config rejection) |
| Opt-in Region global endpoints with OAC | Documented; AWS answers 403, which fails closed |
| Imported buckets cannot be inspected | Documented; deploy-time failure surfaces as a `Failed` revision |
| `RateLimit` supported values vary by Region | Validate the documented grid only; document |
| Fixed-name replacement failures (function, endpoint, log group) | Replacement table + docs; golden templates prevent accidental drift |
| Out-of-band resource-policy edits are overwritten | Documented single-owner contract |
| Revision cleanup quota (50) under frequent deploys | AWS cleans up automatically; documented |
| AWS service still new (behavior, Regions, quotas changing) | Required sandbox deployment before every release that changes these constructs (§19.1) |
| CDK default-rule validation warnings for Web Function types | Document; re-check on 2.274+ |
| No customer-managed revision key in v1 | Documented deferral and L1 escape hatch (§8.7) |

---

## 18. Open questions

Maintainer decisions (resolved in revision 3):

1. Default log retention: **`TWO_WEEKS`**, following `LambdaFunctionConstruct`.
   `WebFunctionLogs.Create(RetentionDays.ONE_MONTH)` remains available.
2. Blazor Static SSR: **required deployed validation** in the release gate (§19.1, item 3.8), using the Node.js
   `execve` shim approach. A minimal rendered SSR page and one successfully served static asset are sufficient.
3. `GrantInvoke` with unresolved accounts: **no identity policies on imported principals whose account cannot be
   established**. Fail under `Automatic`, or use the explicit `ExternallyManaged` contract (§10.2).

Implementation-phase questions (no maintainer input needed unless the result changes scope):

4. T4a, optional: can a narrow, warning-only `AllViewer` check be done through public CDK APIs without resolving
   templates? If not, drop it.
5. Does removing `MaxEnvironments`/`RateLimit` after deployment need an explicit `{}` in the template to clear the
   value (CloudFormation: omit = keep)? Decide in T2, test it, and verify it in the sandbox.
6. Metric helpers on `AWS/Lambda/Web` (namespace confirmed only by Sharedale's working alarms) are a follow-up.
7. Reconcile the stale `VersionPrefix` 2.4.0 in `Directory.Build.props` with tag v2.5.0 at release time.

---

## 19. Suggested implementation breakdown

| # | Task | Depends on | Notes |
|---|---|---|---|
| T0 | Bump `Amazon.CDK.Lib` to 2.274.0; golden templates for the four existing constructs; confirm zero diff | — | Separate PR; establishes deployment-compatibility enforcement |
| T1 | Models: enums, `WebFunctionRuntime`, `WebFunctionCode.FromBucket` + options, `WebFunctionLogs`, endpoint options/attributes; validation local to the new types | T0 | Unit tests for every §14 rule on these types |
| T2 | `WebFunctionConstruct`: function, revision, default endpoint, role, logs, code-bucket policy, dependencies, child IDs; golden templates | T1 | |
| T3 | Endpoints: `WebFunctionEndpointConstruct` (`Resource`, `IResourceWithPolicy`), `AddEndpoint`, single-owner resource policy, `GrantInvoke` (§10.2 algorithm), `GrantCloudFrontInvoke`, `FromEndpointAttributes` | T2 | IAM and policy test matrix |
| T4 | `WebFunctionOrigin` (OAC, automatic/explicit permissions, imported and cross-stack guards) | T3 | Origin test matrix |
| T4a | *Optional*, time-boxed spike: narrow `AllViewer` warning (§11.1) | T4 | Drop if it needs template resolution |
| T5 | Testing helpers: assertions, props builder, `CdkTestHelper` entry; consumer-example and workload tests | T3, T4 | |
| T6 | Docs: `docs/constructs/web-function.md` + nav, three workload recipes, examples, testing page, README, CHANGELOG, `CLAUDE.md` | T4 (API stable) | Can run in parallel with T5 |
| T7 | **Required** sandbox deployment and smoke test (§19.1); record results in the release PR | T4, T6 recipes | Release gate; not normal CI |

T1–T4 can ship as one feature PR, or T0 + (T1–T3) + (T4–T6). Publishing to NuGet waits for T7.

### 19.1 Release acceptance criteria

The feature is **not published** until all of the following hold:

1. Validation gates pass: build, the full test suite, the coverage threshold, and golden templates (including
   zero diff for existing constructs).
2. **Workload compatibility:** Next.js, ASP.NET Core (Node.js `execve` shim), and **Blazor Static SSR** (same shim
   approach, per `j-d-ha/lambda-web-function-not-node`) synthesize through the same public API, with differences only
   in artifacts and configuration. No workload-specific construct, behavior helper, or packaging logic exists in the
   library.
3. **Sandbox deployment and smoke test** succeeds in a non-production AWS account. It is run manually or by a
   dedicated workflow, never in normal CI, from a small checked-in sample app under `samples/` or `test/` (no
   production code). At minimum it verifies:
   1. **Artifact deployment from the supported S3 model**: zips in a versioned bucket, once with `Automatic` bucket
      policy (an owned bucket) and once with `ExternallyManaged` (a pre-provisioned bucket); revisions read the code.
   2. **Revision activation**: function, revision, and endpoints reach `Active`; a second deployment with a changed
      artifact activates a new revision and moves the endpoints to it.
   3. **IAM-authorized invocation**: a request SigV4-signed by a role granted with `GrantInvoke` returns 2xx.
   4. **Unauthorized invocation is denied**: unsigned → 403; signed by a role without the grant → 403; signed for a
      different endpoint of the same function → 403.
   5. **CloudFront OAC invocation**: requests through the distribution return 2xx; direct unsigned requests to the
      `IamAuth` endpoint return 403.
   6. **Resource-policy behavior**: the deployed policy (`aws lambda-web get-resource-policy`) contains exactly the
      expected statements; a second distribution adds a second scoped statement; removing it removes the statement
      on the next deploy.
   7. **Cross-account side** (if a second sandbox account is available): with an `ExternallyManaged` grant and a
      caller-account identity policy, invocation succeeds; without the identity policy, it is denied. If no second
      account is available, record this as not verified.
   8. **Workloads**: a Next.js page and an ASP.NET Core endpoint serve (through CloudFront, or directly with a
      signed request for the API). **Blazor Static SSR (required):** a minimal server-rendered page and one static
      asset (e.g. `app.css` or a `_framework` file) are served successfully, using the Node.js `execve` shim approach
      from `j-d-ha/lambda-web-function-not-node` (apphost `server` + `index.js` shim + readiness POST). The sample is
      ordinary consumer code; no Blazor-specific library abstraction or packaging logic is introduced.
4. The results (date, account alias, CDK version, revision IDs, observed status codes) are recorded in the release
   PR. Any criterion not verified is listed explicitly.

**Follow-ups (independent, not part of this work):** `StaticSiteConstruct` public bucket → OAC review;
`StaticSiteConstruct` `HostedZone.FromLookup` → accept `IHostedZone`; `LambdaFunctionConstruct` `NONE`-auth Function
URL → IAM/OAC option; IPv6/AAAA support (#97); `WebFunctionCode.FromAsset`; typed revision `EncryptionKey` after
deployed validation; metric helpers; network connectors; weighted revisions; `Signing.SIGV4A_ALWAYS_AMZ_AUTH` once
released; tracking aws-cdk#39434.

---

## 20. References

**Repositories (local paths at time of writing)**

- Sharedale: `infra/Sharedale.Infrastructure/SharedaleHostedStack.cs` (Web Functions 526–577; grants 230–252;
  OAC/origin/distribution 327–379; resource policy 381–403; DNS 405–418), `SharedaleAccountBootstrapStack.cs`
  (artifact bucket 113–167), `infra/web-functions/{next-index.js,dotnet-index.js,readiness-mock.js}`,
  `infra/scripts/build-web-functions.sh`, `app/lib/server/api/web-function-iam.ts`,
  `docs/adr/0009…`, `0010…`, `0011…`, `docs/architecture/hosted-environment.md`,
  `docs/architecture/lambda-web-functions-poc.md`.
- LayeredCraft.Cdk.Constructs: `src/LayeredCraft.Cdk.Constructs/{LambdaFunctionConstruct,StaticSiteConstruct,CognitoUserPoolConstruct}.cs`,
  `Models/CognitoUserPoolDomainProps.cs`, `Testing/*`, `test/LayeredCraft.Cdk.Constructs.Tests/*`, `CLAUDE.md`,
  `Directory.Packages.props`, `mkdocs.yml`.
- Blazor/.NET shim reference: <https://github.com/j-d-ha/lambda-web-function-not-node> (`src/blazor/AGENTS.md`,
  `src/blazor/BlazorApp/Program.cs`, `src/node-wrapper/index.js`, `infra/lib/web-function.ts`).
- ASP.NET Core hosting: `LayeredCraft.LambdaWebFunctions.Hosting` 0.1.0 (<https://github.com/LayeredCraft/lambda-web-functions-dotnet>).

**GitHub issues and PRs**

- LayeredCraft/cdk-constructs#98 (this design), #97 (IPv6/AAAA), #99 (explicit hosted zone, Cognito).
- aws/aws-cdk#39210 (WebFunction + NetworkConnector L2, open), #39235 / #39234 (alpha module, open),
  #39434 (Web Function origin with OAC, open), #39196 (`ALWAYS_AMZ_AUTH`, v2.274.0), #38629 (`SIGV4A`, v2.274.0),
  #39216 (`Signing.SIGV4A_ALWAYS_AMZ_AUTH`, merged 2026-10-22, unreleased), #39195.

**AWS documentation (read 2026-10-22)**

- Lambda Web Functions: <https://docs.aws.amazon.com/lambda/latest/dg/web-functions.html>, `web-functions-runtimes.html`,
  `web-functions-how-it-works.html`, `web-functions-revisions.html`, `web-functions-scaling.html`,
  `web-functions-networking.html`, `web-functions-code-s3.html`,
  `web-functions-endpoint-access-control.html` (same base path).
- CloudFront: <https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-restricting-access-to-lambda-web.html>.
- CloudFormation: `AWS::Lambda::WebFunction`, `WebFunctionRevision` (+ `BuildConfig`, `ServiceConfig`),
  `WebFunctionEndpoint` (+ `RevisionWeight`, `ScalingConfig`, `ThrottleConfig`), `AWS::Lambda::ResourcePolicy`, under
  <https://docs.aws.amazon.com/AWSCloudFormation/latest/TemplateReference/>.
- CDK source (v2.274.0): `packages/aws-cdk-lib/aws-iam/lib/grant.ts` (`addToPrincipalOrResource` 149–182, `success`/`assertSuccess` 369–379), `aws-iam/lib/role.ts` (`fromRoleArn` 347–391).
- CDK API: `Amazon.CDK.Lib` 2.273.0 / 2.274.0 XML docs (`CfnWebFunction*`, `CfnResourcePolicy`,
  `FunctionUrlOriginAccessControl`, `Signing`, `SigningBehavior`, `SigningProtocol`, `OriginBindOptions`).

Not verified in the docs pages read (treated as consumer concerns): arm64-only execution, the runtime readiness
callback `/2025-09-01/ready`, the `AWS/Lambda/Web` metric namespace, the region availability list, and the
CloudFormation `RuntimeConfig`/`CodeConfig` allowed-value pages.

---

## 21. Revision history

**Revision 2 (2026-10-22): first-review feedback.**

1. *Public API correctness.* The builder no longer takes `Action<WebFunctionEndpointOptions>` (incompatible with
   init-only records). It follows the repository's builder convention instead (`WithEndpoint(IWebFunctionEndpointOptions)`,
   `WithIamAuthEndpoint()`, `WithTestArtifact(scope)`, `Build()` throws without code or auth). Other changes:
   `HttpOriginProps` pass-through replaced by explicit `WebFunctionOriginProps` members; `ILogGroup?`/`CreateLogGroup`/
   retention flags replaced by the closed `WebFunctionLogs` choice; `EndpointName` is non-nullable with default
   `"default"` everywhere; public `AddToResourcePolicy` exists only on endpoints (endpoint-ARN scoped); assertions
   match by names, not logical-ID prefixes; ownership semantics written out (§8.4).
2. *KMS.* Revision `EncryptionKey` deferred with rationale and an escape hatch; S3 artifact encryption distinguished
   and kept (§5.2, §8.7).
3. *Cross-account grants.* CDK `Grant` source verified. `AddToPrincipalOrResource`/`AssertSuccess` dropped for an
   explicit algorithm that checks `StatementAdded`. A per-grantee table states which policies the library writes and
   what consumers must configure. Cross-account invocation is never reported as configured (§10.2).
4. *Upstream L2.* Guarantees split into C# API stability and deployment compatibility. Logical IDs are necessary
   but not sufficient; full golden-template equivalence is required (§12.4).
5. *Log retention.* Default `TWO_WEEKS` (library convention), with rationale; 30 days is an override; the decision
   is flagged (§8.3, §18).
6. *CloudFront validation.* Opt-in Region list and deep distribution inspection removed; validation scoped to the
   library's own inputs and ownership; T4a optional and warning-only (§11.1, §14).
7. *Deployed validation.* A required sandbox deployment and smoke test is now a release acceptance criterion
   (§19.1).
8. *Blazor Static SSR.* Kept as an explicit acceptance criterion, with no Blazor-specific code (§15, §19.1).
9. *ADRs.* None written. This document is authoritative (header).

**Revision 3 (2026-10-22): final decisions; status Approved.**

1. Default log retention fixed at `TWO_WEEKS` (`LambdaFunctionConstruct` convention); 30 days via
   `WebFunctionLogs.Create(RetentionDays.ONE_MONTH)`.
2. Blazor Static SSR deployed smoke test confirmed as a required release criterion: a minimal SSR page and a static
   asset, using the Node.js shim approach. No Blazor-specific abstractions.
3. `GrantInvoke` ambiguity resolved. Identity policies are written only to principals whose account is established
   (owned principals, or imported principals with literal accounts matching the endpoint's). Anything not fully
   configurable fails under `Automatic` or requires the new explicit `WebFunctionInvokeGrantOptions { CallerPolicy =
   ExternallyManaged }` contract. That contract writes only the resource-policy statement. No synthesized identity
   policy can fail at deployment because its role lives in another account (§10.2, §14, §15).

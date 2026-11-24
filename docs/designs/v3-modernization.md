# Design: LayeredCraft.Cdk.Constructs 3.0.0

| | |
|---|---|
| **Status** | **Proposed** (revision 1, 2026-11-24), awaiting owner review. Approval of this document is an acceptance criterion of [#109](https://github.com/LayeredCraft/cdk-constructs/issues/109); it does not authorize [#110](https://github.com/LayeredCraft/cdk-constructs/issues/110), [#111](https://github.com/LayeredCraft/cdk-constructs/issues/111) or [#112](https://github.com/LayeredCraft/cdk-constructs/issues/112). |
| **Milestone** | [v3.0.0](https://github.com/LayeredCraft/cdk-constructs/milestone/1), epic [#108](https://github.com/LayeredCraft/cdk-constructs/issues/108) |
| **Authority** | This document is the design record for 3.0.0. No standalone ADRs are written. |
| **Target frameworks** | `net10.0`, `net11.0` |
| **AWS CDK floor** | `Amazon.CDK.Lib` 2.278.0 |

> This page is a design record, not user documentation. It is not in the MkDocs `nav`.

---

## 1. Summary

3.0.0 is a major release with two kinds of breaking change, which the release notes and the migration
guide keep separate:

- **Intentional:** .NET 8 and .NET 9 are dropped ([§3](#3-framework-sdk-and-dependency-policy)); the
  `LambdaFunctionConstruct` defaults change to alias-only invocation permissions and a logging policy
  scoped to its own log group ([§7](#7-lambdafunctionconstruct-u1u4-u7-110)).
- **Upgrade-induced:** the `Amazon.CDK.Lib` 2.278.0 floor, and the type-name collisions it introduces
  with this library's Web Function models ([§4](#4-upgrade-induced-changes-amazoncdklib-22780)).

Everything else is additive: an alias name option, public alias and version, S3 artifact code, and a
new private static-assets construct. The order is fixed by the epic: #109 (this modernization, no
construct output change) → #110 and #111 in parallel → #112 (downstream validation) → the owner's
stable release.

**Dogfooding is a requirement** (owner, 2026-11-24): LayeredCraft consumers (Jedi Duel first) must be
able to use these constructs instead of raw CDK. Gaps are closed here, not worked around downstream,
and APIs are not compromised to avoid a breaking change.

## 2. Scope of each issue

| Issue | Delivers | Construct output change |
|---|---|---|
| #109 | This document; net10/net11; SDK pin; every dependency current; Compono tests; devops-templates v11.2 | **None** — golden templates byte-identical |
| #110 | U1 alias name, U2 permission targets (alias-only default), U3 public alias/version, U4 scoped log policy, U7 S3 artifact code | U2 default and U4 policy only |
| #111 | U6 private static-assets construct | New templates only |
| #112 | Downstream validation against one `3.0.0-preview.N`; migration guide; changelog | — |

## 3. Framework, SDK and dependency policy

- **Target frameworks:** `net10.0;net11.0` for the library and the tests. .NET 8 and .NET 9 are
  removed. This is an intentional break: a consumer still on .NET 8 or 9 stays on 2.x.
- **SDK:** `global.json` pins `11.0.100` with `rollForward: latestFeature` and
  `allowPrerelease: false`; `test.runner` stays `Microsoft.Testing.Platform`. The pin governs building
  this repository, not consumers.
- **Dependencies:** the latest compatible stable release of each, as of 2026-11-24:

  | Package | 2.6.0 | 3.0.0 |
  |---|---|---|
  | `Amazon.CDK.Lib` | 2.274.0 | **2.278.0** (consumer floor) |
  | `Microsoft.SourceLink.GitHub` | 10.0.402 | 11.0.100 |
  | `xunit.v3.mtp-v2` | 3.2.2 | 4.0.3 |
  | `Microsoft.NET.Test.Sdk` | 18.11.0 | 18.12.0 (removal decided in #109, [§6](#6-test-stack)) |
  | `xunit.runner.visualstudio` | 3.1.5 | 4.0.1 (removal decided in #109) |
  | `Microsoft.Testing.Extensions.CodeCoverage` | 18.12.0 | 18.12.0 (current) |
  | `AwesomeAssertions` | 9.6.0 | 9.6.0 (current) |
  | `AutoFixture`, `AutoFixture.AutoNSubstitute`, `AutoFixture.Xunit3`, `NSubstitute` | present | **removed** |
  | `Compono`, `Compono.XunitV3` | — | 1.4.0 |

- **No prerelease dependencies.** Any exception needs a written justification and owner approval.
- **Every `Amazon.CDK.Lib` bump** runs the focused real-CDK Web Function security tests
  (`CdkUpgradeGuard_*`) and the golden templates before it is accepted (existing rule, `CLAUDE.md`).
- **Workflows:** LayeredCraft devops-templates **v11.2** for pr-build, publish-preview,
  publish-release, release-drafter and pr-title-check. The v11 PR build is credential-free
  (`contents: read`, no secrets, `runCdk` removed). .NET 10.0.x and 11.0.x are installed. Action pins
  in `docs.yml` and `dependabot-auto-merge.yml` move to their latest stable releases; the latter keeps
  SHA pinning and its `pull_request_target` actor and repository guard. This supersedes Dependabot
  #101.
- **`VersionPrefix`** in `Directory.Build.props` (stale at 2.4.0) becomes `3.0.0`. It only affects
  local builds: CI passes `/p:Version` explicitly ([§5](#5-preview-and-versioning-strategy)).

## 4. Upgrade-induced changes (`Amazon.CDK.Lib` 2.278.0)

### 4.1 Type-name collisions

2.278.0 adds AWS's own L2 Web Function constructs to `Amazon.CDK.AWS.Lambda`. Eight of their type names
match this library's: `IWebFunctionEndpoint` (namespace `LayeredCraft.Cdk.Constructs`) and
`WebFunctionCode`, `WebFunctionRuntime`, `WebFunctionAuthType`, `WebFunctionEndpointType`,
`WebFunctionEndpointOptions`, `IWebFunctionEndpointOptions` and `WebFunctionEndpointAttributes`
(namespace `LayeredCraft.Cdk.Constructs.Models`).

- **Inside the library (#109):** file-local `using` aliases keep our types unambiguous. The public API
  and the CloudFormation output are unchanged.
- **For consumers:** a file that imports both `Amazon.CDK.AWS.Lambda` and our namespaces gets
  `CS0104` (ambiguous reference) and must add the same aliases. This is source-breaking only; no
  binary or template change. It is documented in the README, the Web Function page and the migration
  guide.
- **Not done in #109:** renaming our types. Whether to rename, or to adopt AWS's types, belongs to the
  Web Function decision in [§9](#9-decision-record-webfunctionconstruct-and-the-aws-cdk-l2).

### 4.2 Stricter jsii struct validation

The 2.278.0 jsii runtime validates required struct members when a struct crosses into JavaScript. Two
existing calls passed a wider struct than the parameter declares, and now throw:

- `LambdaFunctionConstruct`: `Alias.AddFunctionUrl(new FunctionUrlProps { … })` → `FunctionUrlOptions`
  (`FunctionUrlProps` requires `function`).
- `StaticSiteConstruct`: `Distribution.AddBehavior(…, new BehaviorOptions { … })` →
  `AddBehaviorOptions` (`BehaviorOptions` requires `origin`).

#109 fixes both. The golden templates prove the output is unchanged.

## 5. Preview and versioning strategy

- **Release Drafter is the version source**, as in AlexaVoxCraft (owner, 2026-11-24). On every push to
  `main` except docs-only changes, `publish-preview.yml` (devops-templates v11.2) runs Release Drafter
  in dry-run mode, takes `resolved_version`, strips any prerelease suffix and publishes
  `<resolved>-preview.<run_number>`. `publish-release.yml` publishes the exact tag of a GitHub release.
- **The #109 PR carries the `breaking-change` label** (its title uses `!`, which the autolabeler maps to
  that label). The latest published release is v2.6.0, so after the merge Release Drafter resolves
  `3.0.0` (the `major` resolver), and every following preview is `3.0.0-preview.N` until the stable
  release. No interleaved `2.6.x` or `2.7.0` previews.
- **Verification before merge (#109):** confirm the PR has the `breaking-change` label, that no merged
  PR since v2.6.0 would resolve higher, and that the stale draft v2.6.1 does not override resolution.
  Release Drafter resolves from the last *published* release and the merged PRs' labels, not from the
  draft's name. The first preview's version is checked in the publish run after the owner merges.
- **Stable 3.0.0** is published only by the owner, after #112.

## 6. Test stack

- Compono and Compono.XunitV3 replace AutoFixture, AutoFixture.AutoNSubstitute, AutoFixture.Xunit3 and
  NSubstitute. The `*AutoDataAttribute` classes and `BaseFixtureFactory` become small,
  concern-named composition profiles used through `[Compose<TProfile>]` /
  `[Compose<TProfile, TConfig>]`; multi-row theories use one `[ComposeData…]`. Compono.TestDoubles is
  added only if a test needs a double.
- Every behavior assertion is kept; the test count per TFM is unchanged unless a difference is
  explained test-by-test.
- `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are removed only if MTP-only execution is
  proven in CI (`dotnet test` with the MTP runner) and the IDE. If either cannot be proven, they stay,
  and the reason is recorded in #109.
- The public consumer helpers under `src/LayeredCraft.Cdk.Constructs/Testing/` are library API and
  are not part of the test-stack migration.

## 7. `LambdaFunctionConstruct`: U1–U4, U7 (#110)

API sketches. Final names are settled in #110 review. Every behavior is driven test-first.

| Item | Change | Default | Template impact |
|---|---|---|---|
| **U1** alias name | `string AliasName` on the props | `"live"` (unchanged) | None at the default. The alias's construct id (`{id}-alias`) does not depend on the name, so its logical ID is stable |
| **U2** permission targets | `LambdaPermissionTargets` flags (`Alias`, `Version`, `Function`), applied to each `LambdaPermission` | **`Alias` only** (owner decision) | **Intentional break:** at the default, the `AWS::Lambda::Permission` resources on the function and on the version disappear. Opting into `Alias \| Version \| Function` restores 2.x output exactly |
| **U3** public alias/version | Public `Alias` (`IAlias`) and `Version` (`IVersion`) properties | — | None |
| **U4** scoped log policy | The execution role may only `logs:CreateLogStream` and `logs:PutLogEvents` on the construct's own log group ARN (`logGroup.LogGroupArn`); `logs:CreateLogGroup`, `logs:TagResource` and the `{name}*` wildcard are removed (the construct creates the log group itself) | — | **Intentional change:** policy document only |
| **U7** S3 artifact code | Optional `Code` (`Amazon.CDK.AWS.Lambda.Code`, e.g. `Code.FromBucketV2(bucket, key, …)`) as an alternative to `AssetPath`; exactly one of the two must be set (validated at synthesis) | `AssetPath` (unchanged) | None for `AssetPath`. `Code.FromBucket` gives the deployer no write access and grants the function no bucket access |

The testing helpers (`LambdaFunctionConstructAssertions`, `LambdaFunctionConstructPropsBuilder`),
the docs and the golden templates are updated in #110. Golden-template diffs are limited to U2 and U4
and are listed in the migration guide with before/after examples.

**Open question for #110 (not a 3.0.0 commitment):** whether the construct should expose the Lambda
logging configuration (`LoggingFormat`, application and system log levels). Today it leaves the
service default (`Text`). Jedi Duel's logging investigation found that its reference implementations
(legacy Jedi Duel and trivia-platform) run with that default, so no consumer currently needs the option.
It is listed so the decision is explicit, not to add scope.

## 8. Private static-assets construct: U6 (#111)

A new construct (working name `PrivateStaticAssetsConstruct`) for immutable, versioned web assets
behind CloudFront. `StaticSiteConstruct` is unchanged: it has a public website bucket with Block Public
Access off, requires Route53 and ACM, adds SPA rewrites and prunes everything.

- **Bucket:** Block Public Access fully on, SSE-S3, SSL-only, versioned, no website endpoint. The
  removal policy is configurable and retains by default.
- **CloudFront:** `S3BucketOrigin.WithOriginAccessControl`. The bucket policy is scoped to this
  distribution's `SourceArn`. HTTPS-only by default (redirect optional); `CACHING_OPTIMIZED` by
  default (configurable).
- **Optional DNS:** domain names plus a certificate (or a hosted-zone lookup) and alias records. Off by
  default; the CloudFront domain is used.
- **Optional SPA routing and error rewrites:** off by default.
- **Deployment:** `BucketDeployment` with a destination key prefix, `Prune` (default `false`),
  `RetainOnDelete` (default `true`), cache-control and optional invalidation paths.
- **Public properties:** `Bucket`, `Distribution`, `DomainName`. Testing helpers, golden templates
  (default and custom-domain variants) and docs.

It must express Jedi Duel's deployed development asset stack (private bucket, OAC, HTTPS-only,
`game/<sha>/` prefix, no prune, retain, immutable cache-control) without raw CDK. #112 checks this.

## 9. Decision record: `WebFunctionConstruct` and the AWS CDK L2

**Owner request (2026-11-24):** evaluate whether to retain the custom L1 implementation (A), replace it
with AWS's L2 (B), or refactor it to compose the official L2 while keeping the LayeredCraft-specific
functionality (C). The owner prefers C, provided it supports current behavior without unacceptable
CloudFormation migration risk. **No refactor happens in #109;** any work needs separate approval.

### 9.1 Evidence

- AWS CDK 2.278.0 source (`packages/aws-cdk-lib/aws-lambda/lib/webfunction*.ts`) and the `aws-lambda`
  README "WebFunction" section, read at tag `v2.278.0`.
- A throwaway synthesis spike (not committed): the L2 `WebFunction` configured like our
  `web-function-default` golden case (same stack, construct id `web-function`, function name, imported
  bucket `golden-artifacts`, key `app.zip`, Node.js 24, IAM-auth default endpoint), compared resource by
  resource with `GoldenTemplates/web-function-default.json`.

### 9.2 Comparison

| Area | LayeredCraft `WebFunctionConstruct` (2.6.0) | AWS CDK 2.278.0 L2 `WebFunction` |
|---|---|---|
| **Resources and logical IDs** (default case, same construct id) | `…Function46F1F768`, `…Revision…B2BF6DC1`, `…ExecutionRole7793F0D5`, `…LogGroup5C9D8E36`, `…LocalLogGroup42143EB1`, `…DefaultEndpoint8A7C111C` | `…Function46F1F768`, `…RevisionB2BF6DC1`, `…ServiceRole0296A7AB`, `…LogGroup5C9D8E36`, `…EndpointED416B7D` |
| **Endpoint** | Name `default`; `FunctionName` is the literal name; `AddEndpoint`; `MaxEnvironments`, `RateLimit`; home-Region and multi-Region types | Name `prod`; `FunctionName` is `Fn::GetAtt FunctionArn`; `addEndpoint`; `rateLimit`; `multiRegion`/`perRegion` locations; network-connector constraint |
| **Revision** | `FunctionName` literal; role and log group wired | `FunctionName` is `Ref`; otherwise identical properties in the spike |
| **Code** | `WebFunctionCode.FromBucket(bucket, key, options)`, bucket policy `Automatic` or `ExternallyManaged` | `fromBucket` (adds a policy for in-app buckets, scoped to this function's revisions), plus `fromAssetToManagedBucket` and `fromAssetToProvidedBucket` (custom-resource copy into a versioned bucket) |
| **Execution role** | Optional; `AWSLambdaBasicExecutionRole` plus statements scoped to the managed log groups | Optional; `AWSLambdaBasicExecutionRole` |
| **Logging** | `WebFunctionLogs.Create` (named `/aws/lambda/web/<name>`, 14 days, plus the home-Region `.local` companion, 3 days), `Use`, `ServiceDefault` | One `logGroup`; default is an unnamed group, 731-day retention, **Retain**; no `.local` companion; application/system log levels |
| **Resource policy** | Single `AWS::Lambda::ResourcePolicy` per function | Single `AWS::Lambda::ResourcePolicy` per function (same rule) |
| **Grants** | `GrantInvoke` (identity or resource policy; fails synthesis when an account cannot be established unless `CallerPolicy = ExternallyManaged`), `GrantCloudFrontInvoke`; `CdkUpgradeGuard_*` tests | `endpoint.grants.invoke(grantee)` (same account → identity policy, otherwise resource policy) |
| **CloudFront** | `WebFunctionOrigin` (`IOrigin`, OAC `lambda`/`always`/SigV4 or SigV4A, required resource-policy statement) | **None**: `aws-cloudfront-origins` 2.278.0 has no Web Function origin |
| **Other** | Synthesis-time validation of invalid combinations; testing helpers; customer-managed revision key unsupported | KMS key, network connector (VPC egress), entry point, tags, import by ARN; metrics not modeled |

### 9.3 Migration risk

The deciding factor is CloudFormation replacement in deployed stacks (Sharedale runs two Web Functions
on this construct):

- **Endpoint replacement changes the URL.** `EndpointName` and the endpoint type are create-only. The
  L2's default endpoint has a different logical ID and name (`prod` instead of `default`). Without
  preserving both, the deploy replaces the endpoint and gives it a new `domainName`, which breaks every
  client and CloudFront origin that uses it.
- **Construct path.** The `Function` and `Revision` logical IDs match only when the L2 sits at the same
  construct path as our construct. Composing the L2 as a child (`<id>/<child>/Function`) changes every
  logical ID: CloudFormation would create a new Web Function (new URL) and delete the old one.
  Matching the path means deriving from the L2 (`WebFunction` is not sealed) or overriding logical IDs.
- **Role and log groups.** The L2 role's logical ID differs (replacement, which is benign but repoints
  the revision). Our log groups are kept only if we pass our own group to the L2. The `.local` companion
  stays LayeredCraft-specific either way.
- **Reference expressions.** The L2 writes `Ref`/`GetAtt` where we write literal names. The values
  resolve identically, but whether CloudFormation treats these as updates must be proven on a real
  stack (`cdk diff`), not assumed.

### 9.4 Options

- **A. Retain the L1 implementation.** Zero migration risk. Leaves two Web Function abstractions side
  by side in consumers' namespaces (the [§4.1](#41-type-name-collisions) collisions) and duplicates
  work AWS now maintains (multi-Region endpoints, asset staging, network connectors, KMS).
- **B. Replace with the L2.** Loses `WebFunctionOrigin`, the `.local` companion, named log groups,
  `ExternallyManaged` bucket and caller policies, endpoint-scoped `GrantCloudFrontInvoke` and the
  fail-closed grant validation. It also changes every deployed template (endpoint replacement, new
  URLs). **Rejected.**
- **C. Compose over the L2 (preferred direction).** Keep the LayeredCraft surface (`WebFunctionOrigin`,
  `WebFunctionLogs` including the companion, fail-closed `GrantInvoke`, `GrantCloudFrontInvoke`,
  validation, testing helpers) and delegate the function, revision, endpoints, code staging and
  resource policy to the L2. **Feasible only if** existing logical IDs and endpoint names are preserved
  (derive from the L2 or override IDs, set the default endpoint name to `default`, pass our role and log
  groups). The goal is byte-identical golden templates, or a diff proven to be update-in-place on a
  deployed stack.

### 9.5 Recommendation

1. **3.0.0 (#109):** retain the current implementation (A) with the file-local aliases. No output or
   API change.
2. **Proposed additional scope — needs explicit owner approval:** a new issue, *"WebFunctionConstruct
   over the AWS CDK L2 (C)"*, in two gated steps:
   - **Spike:** a derived or composed prototype that reproduces every existing Web Function golden
     template byte-for-byte, or lists each remaining diff with its CloudFormation update behavior. Plus
     a `cdk diff` against a deployed Sharedale-shaped stack, read-only, in an isolated temporary checkout
     (Sharedale itself is read-only for this work). No implementation commitment.
   - **Decision:** if the spike preserves templates (or proves update-in-place), implement C and decide
     whether our colliding model types are kept, renamed or replaced by AWS's. If the spike cannot
     preserve the endpoint, stay on A and revisit when AWS ships a Web Function CloudFront origin or
     endpoint-name control is confirmed.
3. **Timing:** if C is template-compatible, it is not a breaking change for deployed stacks and can
   follow 3.0.0. If it requires API breaks (for example replacing our model types with AWS's), it should
   land before stable 3.0.0 so the breaking changes ship in one major. The owner decides after the spike.

## 10. Migration guide outline (#112)

1. **.NET 8/9 removed** (intentional) — stay on 2.x or move to .NET 10/11.
2. **`Amazon.CDK.Lib` ≥ 2.278.0** (upgrade-induced) — the floor and the Web Function type-name
   collisions, with the alias snippet.
3. **`LambdaFunctionConstruct` permission targets** (intentional, U2) — the new alias-only default,
   the template before and after, and how to restore 2.x behavior.
4. **`LambdaFunctionConstruct` logging policy** (intentional, U4) — the policy before and after.
5. **New options** (additive) — alias name (U1), public alias/version (U3), S3 artifact code (U7).
6. **New private static-assets construct** (additive, U6) — when to use it instead of
   `StaticSiteConstruct`.
7. **Consumer test projects** — no change required; the library's own tests moved to Compono.

## 11. Open questions for the owner

1. Approve this design (an acceptance criterion of #109).
2. Approve or decline the proposed Web Function L2 spike issue ([§9.5](#95-recommendation)).
3. Confirm U7's shape: `Code` as an alternative to `AssetPath` (additive) rather than replacing
   `AssetPath`.
4. Confirm the working name `PrivateStaticAssetsConstruct` for U6.

## 12. Revision history

| Revision | Date | Change |
|---|---|---|
| 1 | 2026-11-24 | Initial proposal for owner review |

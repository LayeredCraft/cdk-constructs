# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

> **This feature is not released yet.** It is documented here ahead of the version bump the maintainer applies at release time. No released package version is claimed by this entry.

### ✨ Added

**Lambda Web Functions** — deploy an ordinary HTTP server (Next.js, ASP.NET Core, Blazor Static SSR, and so on) from a zip in a versioned S3 bucket, behind a managed HTTPS endpoint:

- **`WebFunctionConstruct`**: owns one Web Function's function, revision, default endpoint, execution role (optional), log group (optional), and single resource-based policy. Supports `AddEndpoint` for additional endpoints.
- **`WebFunctionEndpointConstruct`** (`IWebFunctionEndpoint`): an individual endpoint, with `GrantInvoke` (identity- or resource-policy based), `GrantCloudFrontInvoke`, `AddToResourcePolicy`, and `FromEndpointAttributes` for imported endpoints.
- **`WebFunctionOrigin`**: returns a CloudFront `IOrigin` with origin access control (OAC) and the required resource-policy statement, for `IamAuth` endpoints.
- **Models**: `WebFunctionCode` / `WebFunctionCodeFromBucketOptions` / `WebFunctionCodeBucketPolicy`, `WebFunctionRuntime`, `WebFunctionLogs`, `WebFunctionEndpointOptions` (and `IWebFunctionEndpointOptions`), `WebFunctionEndpointAttributes`, `WebFunctionEndpointType`, `WebFunctionAuthType`, `WebFunctionCallerPolicy` / `WebFunctionInvokeGrantOptions`, `WebFunctionOriginProps` / `WebFunctionOriginWithOacProps` / `WebFunctionOriginPermissions`, and `WebFunctionConstructProps`.
- **Testing helpers**: `WebFunctionConstructAssertions` fluent `Template` assertions and `WebFunctionConstructPropsBuilder` via `CdkTestHelper.CreateWebFunctionPropsBuilder()` (plus `WebFunctionConstructAutoData`).

### 🔄 Changed

- **Minimum AWS CDK version**: `Amazon.CDK.Lib` minimum raised to **2.274.0**. Consumers below 2.274.0 receive an automatic NuGet resolution upgrade. This floor is required for the enum values the OAC signing configuration needs.

### 📖 Notes and Deferrals

- **No changes to existing constructs.** The Lambda Function, Static Site, DynamoDB Table, and Cognito User Pool constructs are unchanged; their golden templates show zero diff.
- **Artifacts** are read from an existing S3 object with `WebFunctionCode.FromBucket`. `WebFunctionCode.FromAsset` is **not available** in this release; the artifact must be uploaded to a versioned bucket before deployment.
- **Customer-managed revision encryption** (`KmsKeyArn`) is **deferred**. S3 artifact encryption (SSE-S3 / SSE-KMS on the code bucket) is supported.
- **Principal-behavior regression rule**: identity-policy write detection and principal classification rely partly on CDK/jsii behavior observed in `Amazon.CDK.Lib` 2.274.0. Every `Amazon.CDK.Lib` upgrade (including Dependabot bumps) must pass the focused real-CDK Web Function security tests — especially the `CdkUpgradeGuard_*` suite — and a failure blocks the upgrade until the signals are re-verified. The library fails closed when authorization cannot be established.
- **Compatibility**: Lambda Web Functions require a Region where the feature is available; CDK 2.274.0 emits `F3006` default-rule validation warnings for the new `AWS::Lambda::WebFunction*` resource types (warnings by default).

## [2.0.0] - 2025-08-06

### ⚠️ BREAKING CHANGES

**OpenTelemetry Layer Configuration:**
- **OpenTelemetry layer is now disabled by default** (was enabled by default in v1.x)
- Added new required interface properties: `Architecture` and `OtelLayerVersion` 

### ✨ Added

- **Configurable OTEL layer architecture**: Support for both `amd64` and `arm64` architectures via `Architecture` property (default: "amd64")
- **Configurable OTEL layer version**: Control OTEL layer version via `OtelLayerVersion` property (default: "0-117-0")
- **Dynamic OTEL layer ARN format**: Now uses configurable architecture and version: `arn:aws:lambda:{region}:901920570463:layer:aws-otel-collector-{architecture}-ver-{version}:1`
- **Testing helpers**: New builder methods `WithOtelLayerVersion()` and `WithArchitecture()` for test configuration

### 🔄 Changed

- **`IncludeOtelLayer` default**: Changed from `true` to `false` **(BREAKING CHANGE)**
- **Updated OTEL layer version**: Now defaults to latest version (0-117-0, was 0-102-1)
- **Updated AWS CDK dependency**: Upgraded to v2.209.1 from v2.206.0
- **Test defaults**: All test attributes and customizations now default to OTEL disabled

### 🐛 Fixed

- **Future-proof OTEL versioning**: No longer requires package updates when AWS releases new OTEL layer versions

### 📖 Migration Guide

#### For users upgrading from v1.x who want to maintain OTEL functionality:

**Before (v1.x - OTEL enabled by default):**
```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy"
    // OTEL was enabled by default
});
```

**After (v2.0+ - OTEL must be explicitly enabled):**
```csharp
var lambda = new LambdaFunctionConstruct(this, "MyLambda", new LambdaFunctionConstructProps
{
    FunctionName = "my-api",
    FunctionSuffix = "prod",
    AssetPath = "./lambda.zip",
    RoleName = "my-api-role",
    PolicyName = "my-api-policy",
    IncludeOtelLayer = true,           // Must be explicitly enabled
    Architecture = "amd64",            // Optional: specify architecture (default: amd64)
    OtelLayerVersion = "0-117-0"       // Optional: specify version (default: latest)
});
```

#### Benefits of the new approach:

1. **Cost Control**: OTEL layer is now opt-in, preventing unexpected observability costs
2. **Architecture Flexibility**: Support for both x86_64 (amd64) and ARM64 architectures
3. **Version Control**: Specify exact OTEL layer versions for consistent deployments
4. **Future Proof**: No more package updates needed when AWS releases new OTEL versions

#### Testing Updates:

**Before:**
```csharp
[LambdaFunctionConstructAutoData] // OTEL enabled by default
public void Test_Lambda_With_OTEL(LambdaFunctionConstructProps props)
```

**After:**
```csharp
[LambdaFunctionConstructAutoData(includeOtelLayer: true)] // Must explicitly enable
public void Test_Lambda_With_OTEL(LambdaFunctionConstructProps props)
```

## [1.0.1] - Previous Release

### 🔄 Changed
- Package dependency updates
- Documentation improvements

---

## Support

- **Issues**: [GitHub Issues](https://github.com/LayeredCraft/cdk-constructs/issues)
- **Discussions**: [GitHub Discussions](https://github.com/LayeredCraft/cdk-constructs/discussions)
- **Documentation**: [https://layeredcraft.github.io/cdk-constructs/](https://layeredcraft.github.io/cdk-constructs/)
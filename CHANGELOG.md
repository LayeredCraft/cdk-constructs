# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
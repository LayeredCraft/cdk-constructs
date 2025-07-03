# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET library that provides reusable AWS CDK constructs for serverless applications. The library is designed specifically for LayeredCraft projects and focuses on Lambda functions with integrated OpenTelemetry support, IAM management, and environment configuration.

## Commands

### Build and Test
```bash
# Build the solution
dotnet build

# Run all tests (preferred method for xUnit v3)
dotnet run --project test/LayeredCraft.Cdk.Constructs.Tests/ --framework net8.0

# Run tests for a specific project
dotnet test test/LayeredCraft.Cdk.Constructs.Tests/

# Run a specific test
dotnet test --filter "Construct_ShouldCreateLambdaFunction"
```

### Package Management
```bash
# Restore packages
dotnet restore

# Add a package reference
dotnet add src/LayeredCraft.Cdk.Constructs/ package PackageName

# Add a test package reference
dotnet add test/LayeredCraft.Cdk.Constructs.Tests/ package PackageName
```

## Architecture

### Core Components

**LambdaFunctionConstruct** (`src/LayeredCraft.Cdk.Constructs/Constructs/LambdaFunctionConstruct.cs`)
- Main CDK construct that creates Lambda functions with comprehensive configuration
- Automatically creates IAM roles and policies with CloudWatch Logs permissions
- Supports OpenTelemetry layer integration via AWS managed layer
- Creates function versions and aliases for deployment management
- Handles Lambda permissions for multiple targets (function, version, alias)

**Configuration Models** (`src/LayeredCraft.Cdk.Constructs/Models/`)
- `LambdaFunctionConstructProps`: Main configuration interface and record for Lambda construct
- `LambdaPermission`: Record for defining Lambda invocation permissions

### Key Design Patterns

1. **Construct Pattern**: Uses AWS CDK construct pattern for reusable infrastructure components
2. **Interface + Record Pattern**: Props classes use both interface and record for flexibility and immutability
3. **Multi-Target Permissions**: Automatically applies permissions to function, version, and alias
4. **OpenTelemetry Integration**: Built-in support for AWS OTEL collector layer
5. **Versioning Strategy**: Creates new versions on every deployment with "live" alias

### Target Frameworks
- .NET 8.0 and .NET 9.0
- Uses AWS CDK v2 (Amazon.CDK.Lib 2.203.1)

## Testing Framework

### Test Structure
- Uses xUnit v3 with AutoFixture for data generation
- AwesomeAssertions for fluent assertions
- NSubstitute for mocking (though not heavily used in current tests)
- AWS CDK Template assertions for infrastructure testing

### Test Architecture
**Custom Attributes**:
- `LambdaFunctionConstructAutoDataAttribute`: AutoFixture attribute with customizable parameters
- `BaseFixtureFactory`: Factory for creating configured AutoFixture instances

**Test Customizations**:
- `LambdaFunctionConstructCustomization`: Configures realistic test data for Lambda constructs
- Supports parameterized test scenarios (OTEL layer on/off, permissions included/excluded)

### Test Data Strategy
- Uses AutoFixture with custom configurations for realistic AWS resource names
- Test assets include `test-lambda.zip` for Lambda deployment packages
- Tests verify both resource creation and property configuration

### Testing Helpers (`src/LayeredCraft.Cdk.Constructs/Testing/`)
The library includes comprehensive testing helpers for consumers:

**CdkTestHelper** (`CdkTestHelper.cs`):
- `CreateTestStack()`: Creates CDK app and stack with test environment
- `CreateTestStack<TStack>()`: Creates custom stack types directly using generics and reflection
- `CreateTestStackMinimal()`: Creates only the stack (app created internally)  
- `CreateTestStackMinimal<TStack>()`: Creates custom stack types with app created internally
- `GetTestAssetPath()`: Resolves test asset paths relative to executing assembly
- `CreatePropsBuilder()`: Creates fluent builder with test defaults

**LambdaFunctionConstructAssertions** (`LambdaFunctionConstructAssertions.cs`):
- Extension methods for Template assertions
- `ShouldHaveLambdaFunction()`, `ShouldHaveOtelLayer()`, `ShouldHaveCloudWatchLogsPermissions()`, etc.

**LambdaFunctionConstructPropsBuilder** (`LambdaFunctionConstructPropsBuilder.cs`):
- Fluent builder for creating test props with common AWS service integrations
- Methods like `WithDynamoDbAccess()`, `WithS3Access()`, `WithApiGatewayPermission()`

**Critical Testing Pattern**: 
- Always create CDK templates AFTER adding constructs to stacks
- Use `Template.FromStack(stack)` after construct creation, not before

**Generic Stack Creation**:
- Use `CreateTestStack<TStack>()` and `CreateTestStackMinimal<TStack>()` for custom stack types
- These methods use `Activator.CreateInstance` with reflection to instantiate custom stacks
- Eliminates the need to create unused base stacks when testing custom stack implementations
- Supports any stack type that inherits from `Amazon.CDK.Stack` and follows the standard constructor pattern

## Development Practices

### Code Style
- C# 12+ features enabled (records, required properties, collection expressions)
- Nullable reference types enabled
- Implicit usings enabled
- Uses `required` properties for mandatory configuration

### NuGet Package Configuration
- Configured for NuGet.org publishing with MIT license
- Includes source linking via Microsoft.SourceLink.GitHub
- Symbol packages (snupkg) enabled for debugging support
- Package metadata includes proper tagging for discoverability

### AWS CDK Patterns
- Uses `PROVIDED_AL2023` runtime for Lambda functions (supports custom runtimes)
- Hardcoded OpenTelemetry layer ARN for us-east-1 region
- Default memory: 1024MB, timeout: 6 seconds, log retention: 2 weeks
- Uses `RemovalPolicy.RETAIN` for Lambda versions to prevent deletion
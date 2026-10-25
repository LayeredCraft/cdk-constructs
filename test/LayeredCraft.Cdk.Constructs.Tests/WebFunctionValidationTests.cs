using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionValidationTests
{
    // -------------------------------------------------------------------------
    // Function name
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("my-function_1")]
    [InlineData("Func-123")]
    public void ValidateFunctionName_AcceptsValidNames(string name)
    {
        Action act = () => WebFunctionValidation.ValidateFunctionName(name);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-start")]
    [InlineData("end-")]
    [InlineData("has space")]
    [InlineData("bad.name")]
    public void ValidateFunctionName_RejectsInvalidNames(string name)
    {
        Action act = () => WebFunctionValidation.ValidateFunctionName(name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateFunctionName_AcceptsMaxLength()
    {
        Action act = () => WebFunctionValidation.ValidateFunctionName(new string('a', 64));

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateFunctionName_RejectsTooLong()
    {
        Action act = () => WebFunctionValidation.ValidateFunctionName(new string('a', 65));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateFunctionName_SkipsTokens()
    {
        var token = Fn.Ref("function-token");

        Action act = () => WebFunctionValidation.ValidateFunctionName(token);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Endpoint name
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("default")]
    [InlineData("a-b_c")]
    [InlineData("Endpoint1")]
    public void ValidateEndpointName_AcceptsValidNames(string name)
    {
        Action act = () => WebFunctionValidation.ValidateEndpointName(name);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad.name")]
    [InlineData("has space")]
    [InlineData("sl/ash")]
    public void ValidateEndpointName_RejectsInvalidNames(string name)
    {
        Action act = () => WebFunctionValidation.ValidateEndpointName(name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEndpointName_RejectsTooLong()
    {
        Action act = () => WebFunctionValidation.ValidateEndpointName(new string('a', 65));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEndpointName_SkipsTokens()
    {
        var token = Fn.Ref("endpoint-token");

        Action act = () => WebFunctionValidation.ValidateEndpointName(token);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Timeout
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(3)]
    [InlineData(30)]
    [InlineData(900)]
    public void ValidateTimeout_AcceptsRange(int seconds)
    {
        Action act = () => WebFunctionValidation.ValidateTimeout(Duration.Seconds(seconds));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(901)]
    public void ValidateTimeout_RejectsOutOfRange(int seconds)
    {
        Action act = () => WebFunctionValidation.ValidateTimeout(Duration.Seconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ValidateTimeout_RejectsFractionalSeconds()
    {
        Action act = () => WebFunctionValidation.ValidateTimeout(Duration.Seconds(2.5));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ValidateTimeout_AcceptsNull()
    {
        Action act = () => WebFunctionValidation.ValidateTimeout(null);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Max concurrency per environment
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    public void ValidateMaxConcurrency_AcceptsRange(int value)
    {
        Action act = () => WebFunctionValidation.ValidateMaxConcurrencyPerEnvironment(value);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(129)]
    public void ValidateMaxConcurrency_RejectsOutOfRange(int value)
    {
        Action act = () => WebFunctionValidation.ValidateMaxConcurrencyPerEnvironment(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ValidateMaxConcurrency_AcceptsNull()
    {
        Action act = () => WebFunctionValidation.ValidateMaxConcurrencyPerEnvironment(null);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Max environments
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(10000)]
    public void ValidateMaxEnvironments_AcceptsRange(int value)
    {
        Action act = () => WebFunctionValidation.ValidateMaxEnvironments(value);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10001)]
    public void ValidateMaxEnvironments_RejectsOutOfRange(int value)
    {
        Action act = () => WebFunctionValidation.ValidateMaxEnvironments(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // Rate limit
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    [InlineData(10000)]
    public void ValidateRateLimit_AcceptsGridValues(int value)
    {
        Action act = () => WebFunctionValidation.ValidateRateLimit(value);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(50)]
    [InlineData(150)]
    [InlineData(999)]
    [InlineData(1100)]
    [InlineData(1500)]
    [InlineData(10001)]
    public void ValidateRateLimit_RejectsOffGridValues(int value)
    {
        Action act = () => WebFunctionValidation.ValidateRateLimit(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // Regions
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateRegions_HomeRegion_AcceptsEmpty()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(WebFunctionEndpointType.HomeRegion, []);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_HomeRegion_AcceptsNull()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(WebFunctionEndpointType.HomeRegion, null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_HomeRegion_AcceptsHomeRegionOnly()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.HomeRegion,
            ["us-east-1"],
            "us-east-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_HomeRegion_RejectsForeignRegion()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.HomeRegion,
            ["us-west-2"],
            "us-east-1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_HomeRegion_RejectsMultipleRegionsWhenHomeUnknown()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.HomeRegion,
            ["us-east-1", "us-west-2"]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_MultiRegion_RequiresRegions()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(WebFunctionEndpointType.MultiRegion, []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_MultiRegion_RejectsHomeRegionOnly()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            ["us-east-1"],
            "us-east-1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_MultiRegion_AcceptsAdditionalRegion()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            ["us-east-1", "us-west-2"],
            "us-east-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_PerRegion_RequiresRegions()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(WebFunctionEndpointType.PerRegion, []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_RejectsDuplicates()
    {
        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            ["us-east-1", "us-east-1"],
            "us-west-2");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_RejectsTooMany()
    {
        var regions = Enumerable.Range(0, 51).Select(index => $"region-{index}").ToArray();

        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            regions,
            "us-east-1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegions_SkipsTokens()
    {
        var token = Fn.Ref("region-token");

        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            [token]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_MultiRegion_DefersHomeRegionCheckWhenAllRegionsAreTokens()
    {
        var token = Fn.Ref("region-token");

        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            [token],
            "us-east-1");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRegions_MultiRegion_DefersHomeRegionCheckWhenTokenMayResolveToNonHomeRegion()
    {
        var token = Fn.Ref("region-token");

        Action act = () => WebFunctionValidation.ValidateRegions(
            WebFunctionEndpointType.MultiRegion,
            [token, "us-east-1"],
            "us-east-1");

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Environment variables
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("MY_VAR")]
    [InlineData("A")]
    [InlineData("a1_b2")]
    [InlineData("AWS_LAMBDA_NODEJS_ENTRYPOINT")]
    public void ValidateEnvironmentVariableKey_AcceptsValidKeys(string key)
    {
        Action act = () => WebFunctionValidation.ValidateEnvironmentVariableKey(key);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1VAR")]
    [InlineData("MY-VAR")]
    [InlineData("has space")]
    [InlineData("_LEADING")]
    [InlineData("AWS_LAMBDA_HTTP_ENDPOINT")]
    public void ValidateEnvironmentVariableKey_RejectsInvalidKeys(string key)
    {
        Action act = () => WebFunctionValidation.ValidateEnvironmentVariableKey(key);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEnvironmentVariableKey_SkipsTokens()
    {
        var token = Fn.Ref("env-token");

        Action act = () => WebFunctionValidation.ValidateEnvironmentVariableKey(token);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateEnvironmentVariables_ValidatesEveryKey()
    {
        var environmentVariables = new Dictionary<string, string>
        {
            ["OK"] = "1",
            ["1BAD"] = "2",
        };

        Action act = () => WebFunctionValidation.ValidateEnvironmentVariables(environmentVariables);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEnvironmentVariables_AcceptsNull()
    {
        Action act = () => WebFunctionValidation.ValidateEnvironmentVariables(null);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Entry point
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("index.js")]
    [InlineData("src/index.js")]
    [InlineData("a/b/c.js")]
    public void ValidateEntryPoint_AcceptsValidEntryPoints(string? entryPoint)
    {
        Action act = () => WebFunctionValidation.ValidateEntryPoint(entryPoint, null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("/index.js")]
    [InlineData("../index.js")]
    [InlineData("a/../b.js")]
    public void ValidateEntryPoint_RejectsInvalidEntryPoints(string entryPoint)
    {
        Action act = () => WebFunctionValidation.ValidateEntryPoint(entryPoint, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEntryPoint_RejectsConflictWithNodeJsEntryPointVariable()
    {
        var environmentVariables = new Dictionary<string, string>
        {
            ["AWS_LAMBDA_NODEJS_ENTRYPOINT"] = "index.js",
        };

        Action act = () => WebFunctionValidation.ValidateEntryPoint("index.js", environmentVariables);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEntryPoint_SkipsTokenEntryPoint()
    {
        var token = Fn.Ref("entry-token");

        Action act = () => WebFunctionValidation.ValidateEntryPoint(token, null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateEntryPoint_TokenConflictStillThrows()
    {
        var token = Fn.Ref("entry-token");
        var environmentVariables = new Dictionary<string, string>
        {
            ["AWS_LAMBDA_NODEJS_ENTRYPOINT"] = "index.js",
        };

        Action act = () => WebFunctionValidation.ValidateEntryPoint(token, environmentVariables);

        act.Should().Throw<ArgumentException>();
    }
}

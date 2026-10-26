using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using Annotations = Amazon.CDK.Assertions.Annotations;
using Match = Amazon.CDK.Assertions.Match;
using RuntimeAnnotations = Amazon.CDK.Annotations;
using Template = Amazon.CDK.Assertions.Template;

namespace LayeredCraft.Cdk.Constructs.Tests;

[Collection("CDK Tests")]
public class WebFunctionCompanionLogGroupTests
{
    private const string UnmanagedCompanionWarningId =
        "@layeredcraft/cdk-constructs:webFunctionUnmanagedCompanionLogGroup";

    private const string RemoteCompanionWarningId =
        "@layeredcraft/cdk-constructs:webFunctionRemoteCompanionLogGroups";

    private const string LogGroupType = "AWS::Logs::LogGroup";
    private const string RevisionType = "AWS::Lambda::WebFunctionRevision";
    private const string PolicyType = "AWS::IAM::Policy";

    // ------------------------------------------------------------------ Synthesis

    [Fact]
    public void T1_Create_ShouldCreateCentralizedAndCompanionLogGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var construct = CreateWebFunction(stack, WebFunctionLogs.Create());

        var template = TemplateJson(stack);
        var groups = ResourcesOfType(template, LogGroupType);
        groups.Should().HaveCount(2);

        var centralized = FindLogGroup(groups, "/aws/lambda/web/test-web-function");
        RetentionOf(centralized).Should().Be(14);
        DeletionPolicyOf(centralized).Should().Be("Delete");

        var companion = FindLogGroup(groups, "/aws/lambda/web/test-web-function.local");
        RetentionOf(companion).Should().Be(3);
        DeletionPolicyOf(companion).Should().Be("Delete");

        construct.LogGroup.Should().NotBeNull();
        construct.LocalLogGroup.Should().NotBeNull();
    }

    [Fact]
    public void T2_Create_ShouldApplyConfiguredRetentionAndRemovalPolicyToBothGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        CreateWebFunction(
            stack,
            WebFunctionLogs.Create(RetentionDays.ONE_MONTH, RemovalPolicy.RETAIN, RetentionDays.ONE_DAY));

        var template = TemplateJson(stack);
        var groups = ResourcesOfType(template, LogGroupType);
        groups.Should().HaveCount(2);

        var centralized = FindLogGroup(groups, "/aws/lambda/web/test-web-function");
        RetentionOf(centralized).Should().Be(30);
        DeletionPolicyOf(centralized).Should().Be("Retain");

        var companion = FindLogGroup(groups, "/aws/lambda/web/test-web-function.local");
        RetentionOf(companion).Should().Be(1);
        DeletionPolicyOf(companion).Should().Be("Retain");
    }

    [Fact]
    public void T3_Create_ShouldMakeRevisionDependOnBothLogGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        CreateWebFunction(stack, WebFunctionLogs.Create());

        var template = TemplateJson(stack);
        var centralizedId = LogicalIdOfLogGroup(template, "/aws/lambda/web/test-web-function");
        var companionId = LogicalIdOfLogGroup(template, "/aws/lambda/web/test-web-function.local");

        var revision = RevisionOf(template, "test-web-function");
        var dependsOn = DependsOnIds(revision);

        dependsOn.Should().Contain(centralizedId);
        dependsOn.Should().Contain(companionId);
    }

    [Fact]
    public void T4_Create_ShouldRenderTokenFunctionNameCompanionAsJoin()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        CreateWebFunction(stack, WebFunctionLogs.Create(), functionName: Fn.ImportValue("web-function-name"));

        var template = TemplateJson(stack);
        var groups = ResourcesOfType(template, LogGroupType);
        groups.Should().HaveCount(2);

        var companion = groups
            .Select(g => g.Value)
            .Where(g =>
                Properties(g).TryGetProperty("LogGroupName", out var name)
                && name.ValueKind == JsonValueKind.Object)
            .Single(g => Properties(g).GetProperty("LogGroupName").GetRawText().Contains(".local", StringComparison.Ordinal));

        var raw = Properties(companion).GetProperty("LogGroupName").GetRawText();
        raw.Should().Contain("Fn::Join");
        raw.Should().Contain("web-function-name");
        raw.Should().Contain(".local");
    }

    [Fact]
    public void T5_LocalLogGroup_ShouldBeExposedOnlyWhenManaged()
    {
        var (_, createStack) = CdkTestHelper.CreateTestStack();
        var createConstruct = CreateWebFunction(createStack, WebFunctionLogs.Create());
        createConstruct.LocalLogGroup.Should().NotBeNull();
        createConstruct.LocalLogGroup!.Node.Path.Should().EndWith("/LocalLogGroup");

        var (_, useCompanionStack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(useCompanionStack, "Centralized", new LogGroupProps { LogGroupName = "/custom/logs" });
        var companion = new LogGroup(useCompanionStack, "Companion", new LogGroupProps { LogGroupName = "/custom/logs.local" });
        var useCompanionConstruct = CreateWebFunction(useCompanionStack, WebFunctionLogs.Use(centralized, companion));
        useCompanionConstruct.LocalLogGroup.Should().BeSameAs(companion);

        var (_, useOnlyStack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(useOnlyStack, "External", "/custom/logs");
        var useOnlyConstruct = CreateWebFunction(useOnlyStack, WebFunctionLogs.Use(imported));
        useOnlyConstruct.LocalLogGroup.Should().BeNull();

        var (_, serviceStack) = CdkTestHelper.CreateTestStack();
        var serviceConstruct = CreateWebFunction(serviceStack, WebFunctionLogs.ServiceDefault());
        serviceConstruct.LocalLogGroup.Should().BeNull();
    }

    [Fact]
    public void T6_Use_ShouldNotCreateLogGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        var template = TemplateJson(stack);
        ResourcesOfType(template, LogGroupType).Should().BeEmpty();

        var revision = RevisionOf(template, "test-web-function");
        var loggingConfig = Properties(revision)
            .GetProperty("ServiceConfig")
            .GetProperty("TelemetryConfig")
            .GetProperty("LoggingConfig");
        loggingConfig.GetProperty("LogGroup").GetString().Should().Be("/custom/logs");

        construct.LocalLogGroup.Should().BeNull();
    }

    [Fact]
    public void T7_UseWithCompanion_ShouldMakeRevisionDependOnBothSuppliedGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = "/custom/logs" });
        var companion = new LogGroup(stack, "Companion", new LogGroupProps { LogGroupName = "/custom/logs.local" });

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion));

        var template = TemplateJson(stack);
        ResourcesOfType(template, LogGroupType).Should().HaveCount(2);

        var centralizedId = LogicalIdOfLogGroup(template, "/custom/logs");
        var companionId = LogicalIdOfLogGroup(template, "/custom/logs.local");
        var dependsOn = DependsOnIds(RevisionOf(template, "test-web-function"));

        dependsOn.Should().Contain(centralizedId);
        dependsOn.Should().Contain(companionId);
    }

    [Fact]
    public void T8_Use_WithImportedGroups_ShouldNotAddDependencies()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion));

        var template = TemplateJson(stack);

        // Imported groups add no resources and therefore no dependencies.
        ResourcesOfType(template, LogGroupType).Should().BeEmpty();

        var functionId = ResourcesOfType(template, "AWS::Lambda::WebFunction").Single().Key;
        var roleId = ResourcesOfType(template, "AWS::IAM::Role").Single().Key;
        var dependsOn = DependsOnIds(RevisionOf(template, "test-web-function"));

        dependsOn.Should().BeEquivalentTo([functionId, roleId]);
        construct.LocalLogGroup.Should().BeSameAs(companion);
    }

    [Fact]
    public void T8b_Use_WithCrossStackOwnedGroup_ShouldNotAddExplicitDependency()
    {
        var app = new App();
        var producerStack = new Stack(app, "producer", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });
        var centralized = new LogGroup(producerStack, "Centralized", new LogGroupProps { LogGroupName = "/custom/logs" });

        var consumerStack = new Stack(app, "consumer", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" },
        });
        var construct = CreateWebFunction(consumerStack, WebFunctionLogs.Use(centralized));

        var template = TemplateJson(consumerStack);
        ResourcesOfType(template, LogGroupType).Should().BeEmpty();

        var functionId = ResourcesOfType(template, "AWS::Lambda::WebFunction").Single().Key;
        var roleId = ResourcesOfType(template, "AWS::IAM::Role").Single().Key;
        var dependsOn = DependsOnIds(RevisionOf(template, "test-web-function"));

        // Only the consumer stack's own resources are depended on; the cross-stack group is never an explicit
        // dependency (it is referenced through Fn::ImportValue instead).
        dependsOn.Should().BeEquivalentTo([functionId, roleId]);

        // The legitimate cross-stack reference (and its implicit dependency) is preserved.
        template.GetProperty("Resources").EnumerateObject()
            .SelectMany(property => property.Value.GetRawText().Split("\n"))
            .Any(line => line.Contains("Fn::ImportValue", StringComparison.Ordinal))
            .Should()
            .BeTrue();

        construct.LogGroup.Should().BeSameAs(centralized);
    }

    [Fact]
    public void T9_Use_ShouldRejectLiteralCompanionNameMismatch()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var wrongCompanion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/wrong");

        Action act = () => WebFunctionLogs.Use(centralized, wrongCompanion);

        act.Should().Throw<ArgumentException>().WithParameterName("companion");
    }

    [Fact]
    public void T9_Use_ShouldAcceptTokenizedCompanionName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", Fn.ImportValue("centralized-name"));
        var companion = LogGroup.FromLogGroupName(stack, "Companion", Fn.ImportValue("companion-name"));

        Action act = () => WebFunctionLogs.Use(centralized, companion);

        act.Should().NotThrow();
    }

    [Fact]
    public void T9_Use_ShouldRejectOwnedLiteralCompanionNameMismatch()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = "/custom/a" });
        var wrongCompanion = new LogGroup(stack, "Companion", new LogGroupProps { LogGroupName = "/custom/WRONG" });

        Action act = () => WebFunctionLogs.Use(centralized, wrongCompanion);

        act.Should().Throw<ArgumentException>().WithParameterName("companion");
    }

    [Fact]
    public void T9_Use_ShouldAcceptOwnedLiteralCompanionMatch()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = "/custom/a" });
        var companion = new LogGroup(stack, "Companion", new LogGroupProps { LogGroupName = "/custom/a.local" });

        Action act = () => WebFunctionLogs.Use(centralized, companion);

        act.Should().NotThrow();
    }

    [Fact]
    public void T9_Use_ShouldRejectOwnedCentralizedImportedCompanionMismatch()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = "/custom/a" });
        var wrongCompanion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/WRONG");

        Action act = () => WebFunctionLogs.Use(centralized, wrongCompanion);

        act.Should().Throw<ArgumentException>().WithParameterName("companion");
    }

    [Fact]
    public void T9_Use_ShouldAcceptOwnedTokenizedNames()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = Fn.ImportValue("centralized-name") });
        var companion = new LogGroup(stack, "Companion", new LogGroupProps { LogGroupName = Fn.ImportValue("companion-name") });

        Action act = () => WebFunctionLogs.Use(centralized, companion);

        act.Should().NotThrow();
    }

    [Fact]
    public void T9_Use_ShouldAcceptUnnamedOwnedCompanion()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var centralized = new LogGroup(stack, "Centralized", new LogGroupProps { LogGroupName = "/custom/a" });
        var unnamedCompanion = new LogGroup(stack, "Companion");

        Action act = () => WebFunctionLogs.Use(centralized, unnamedCompanion);

        act.Should().NotThrow();
    }

    [Fact]
    public void T10_ServiceDefault_ShouldNotCreateOrConfigureLogGroups()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var construct = CreateWebFunction(stack, WebFunctionLogs.ServiceDefault());

        var template = TemplateJson(stack);
        ResourcesOfType(template, LogGroupType).Should().BeEmpty();

        var revision = RevisionOf(template, "test-web-function");
        Properties(revision).GetProperty("ServiceConfig").TryGetProperty("TelemetryConfig", out _).Should().BeFalse();

        construct.LogGroup.Should().BeNull();
        construct.LocalLogGroup.Should().BeNull();
    }

    // ------------------------------------------------------------------ Permissions

    [Fact]
    public void P1_CreatedRoleWithCreate_ShouldNotReceiveLogStatements()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        CreateWebFunction(stack, WebFunctionLogs.Create());

        var template = TemplateJson(stack);
        AllPolicyStatements(template).Should().BeEmpty();

        var role = ResourcesOfType(template, "AWS::IAM::Role").Single().Value;
        var managed = Properties(role).GetProperty("ManagedPolicyArns").GetRawText();
        managed.Should().Contain("AWSLambdaBasicExecutionRole");
    }

    [Fact]
    public void P2_SuppliedRoleWithCustomUse_ShouldGrantExistingWritePlusCompanionWriteAndCreateGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);

        var template = TemplateJson(stack);
        var statements = AllPolicyStatements(template);

        // GrantWrite (existing behavior) + companion write (G1) + companion create group (G2).
        statements.Should().HaveCount(3);
        statements.Should().NotContain(s => Actions(s).Contains("logs:*"));

        var createGroup = statements.Single(s => Actions(s).Contains("logs:CreateLogGroup"));
        Actions(createGroup).Should().BeEquivalentTo(new[] { "logs:CreateLogGroup" });
        ResourceArns(createGroup).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:us-east-1:123456789012:log-group:/custom/logs.local",
        });

        var companionWrite = statements.Single(s => ResourceText(s).Contains(".local:log-stream:*"));
        Actions(companionWrite).Should().BeEquivalentTo(new[] { "logs:CreateLogStream", "logs:PutLogEvents" });
        ResourceArns(companionWrite).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:us-east-1:123456789012:log-group:/custom/logs.local:log-stream:*",
        });
    }

    [Fact]
    public void P3_SuppliedRoleWithCompanion_ShouldGrantOnlyCompanionWrite()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);

        var template = TemplateJson(stack);
        var statements = AllPolicyStatements(template);

        statements.Should().NotContain(s => Actions(s).Contains("logs:CreateLogGroup"));
        statements.Count(s => ResourceText(s).Contains(".local:log-stream:*")).Should().Be(1);
    }

    [Fact]
    public void P4_CompanionAndPerRegionEndpoint_ShouldWidenAndAddCreateGroupOnce()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);
        construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        var template = TemplateJson(stack);
        var statements = AllPolicyStatements(template);

        var companionWrite = statements.Where(s => ResourceText(s).Contains(".local:log-stream:*")).ToList();
        companionWrite.Should().HaveCount(1);
        ResourceText(companionWrite[0]).Should().Contain(":logs:*:");

        var createGroup = statements.Where(s => Actions(s).Contains("logs:CreateLogGroup")).ToList();
        createGroup.Should().HaveCount(1);
        ResourceText(createGroup[0]).Should().Contain(":logs:*:");
        ResourceText(createGroup[0]).Should().Contain(":log-group:/custom/logs.local");
    }

    [Fact]
    public void P5_ImportedMutableRole_ShouldReceiveStatementsInPolicyAttachedByRoleName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = Role.FromRoleArn(stack, "Imported", "arn:aws:iam::123456789012:role/caller");
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);

        var template = TemplateJson(stack);
        var policies = ResourcesOfType(template, PolicyType);
        policies.Should().HaveCount(1);

        var roles = Properties(policies[0].Value).GetProperty("Roles").GetRawText();
        roles.Should().Contain("caller");

        var statements = AllPolicyStatements(template);
        statements.Count(s => ResourceText(s).Contains(".local:log-stream:*")).Should().Be(1);
        statements.Should().Contain(s => Actions(s).Contains("logs:CreateLogGroup"));
    }

    [Fact]
    public void P6_ImportedImmutableRole_ShouldNotReceivePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = Role.FromRoleArn(
            stack,
            "Imported",
            "arn:aws:iam::123456789012:role/caller",
            new FromRoleArnOptions { Mutable = false });
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        Action act = () => CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);
        act.Should().NotThrow();

        var template = TemplateJson(stack);
        ResourcesOfType(template, PolicyType).Should().BeEmpty();
    }

    [Fact]
    public void P7_SuppliedRoleWithoutWriteTrigger_ShouldNotReceiveLogStatements()
    {
        var (_, createStack) = CdkTestHelper.CreateTestStack();
        var createRole = OwnedRole(createStack);
        CreateWebFunction(createStack, WebFunctionLogs.Create(), createRole);
        AllPolicyStatements(TemplateJson(createStack)).Should().BeEmpty();

        var (_, serviceStack) = CdkTestHelper.CreateTestStack();
        var serviceRole = OwnedRole(serviceStack);
        CreateWebFunction(serviceStack, WebFunctionLogs.ServiceDefault(), serviceRole);
        AllPolicyStatements(TemplateJson(serviceStack)).Should().BeEmpty();

        var (_, lambdaNameStack) = CdkTestHelper.CreateTestStack();
        var lambdaNameRole = OwnedRole(lambdaNameStack);
        var awsLambdaGroup = LogGroup.FromLogGroupName(lambdaNameStack, "Centralized", "/aws/lambda/web/external");
        CreateWebFunction(lambdaNameStack, WebFunctionLogs.Use(awsLambdaGroup), lambdaNameRole);
        AllPolicyStatements(TemplateJson(lambdaNameStack)).Should().BeEmpty();
    }

    [Fact]
    public void P8_TokenizedCustomName_ShouldScopeCompanionResourcesAsJoin()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", Fn.ImportValue("log-group-name"));

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);

        var template = TemplateJson(stack);
        var statements = AllPolicyStatements(template);

        var companionWrite = statements.Where(s => Actions(s).Contains("logs:CreateLogStream")).ToList();
        companionWrite.Should().HaveCount(2);
        companionWrite.Should().Contain(s => ResourceText(s).Contains("Fn::Join") && ResourceText(s).Contains("log-group-name"));

        var createGroup = statements.Single(s => Actions(s).Contains("logs:CreateLogGroup"));
        ResourceText(createGroup).Should().Contain("Fn::Join");
        ResourceText(createGroup).Should().Contain("log-group-name");
    }

    [Fact]
    public void P9_NoTemplate_ShouldContainWildcardActionsOrCentralizedCreateGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);
        construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.MultiRegion,
            Regions = ["us-west-2"],
        });

        var template = TemplateJson(stack);
        var statements = AllPolicyStatements(template);

        statements.SelectMany(Actions).Should().NotContain("logs:*");
        statements.Select(ResourceText).Should().NotContain(text => text.Contains(":log-group:*", StringComparison.Ordinal));

        foreach (var createGroup in statements.Where(s => Actions(s).Contains("logs:CreateLogGroup")))
            ResourceText(createGroup).Should().Contain(".local");
    }

    [Fact]
    public void P11_AddPerRegionEndpointAfterConstruction_ShouldWidenSingleStatements()
    {
        AssertOrderingWidens(WebFunctionEndpointType.PerRegion);
        AssertOrderingWidens(WebFunctionEndpointType.MultiRegion);
    }

    [Fact]
    public void P12_MultipleNonHomeEndpoints_ShouldNotDuplicateStatements()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);
        construct.AddEndpoint("first", new WebFunctionEndpointOptions
        {
            EndpointName = "first",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });
        construct.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.MultiRegion,
            Regions = ["eu-west-1"],
        });

        var statements = AllPolicyStatements(TemplateJson(stack));
        statements.Count(s => ResourceText(s).Contains(".local:log-stream:*")).Should().Be(1);
        statements.Count(s => Actions(s).Contains("logs:CreateLogGroup")).Should().Be(1);
    }

    [Fact]
    public void P13_HomeRegionOnlyEndpoints_ShouldKeepStackRegionAndNoCreateGroupWhenCompanionSupplied()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);
        construct.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.HomeRegion,
        });

        var statements = AllPolicyStatements(TemplateJson(stack));
        var companionWrite = statements.Where(s => ResourceText(s).Contains(".local:log-stream:*")).ToList();
        companionWrite.Should().HaveCount(1);
        ResourceText(companionWrite[0]).Should().Contain(":logs:us-east-1:");
        statements.Should().NotContain(s => Actions(s).Contains("logs:CreateLogGroup"));
    }

    [Fact]
    public void P14_UnresolvedStackEnvironment_ShouldUseRegionAndAccountReferences()
    {
        var homeStack = new Stack(new App(), "unfixed-home");
        var homeRole = OwnedRole(homeStack);
        var homeCentralized = LogGroup.FromLogGroupName(homeStack, "Centralized", "/custom/logs");
        CreateWebFunction(homeStack, WebFunctionLogs.Use(homeCentralized), homeRole);

        var homeWrite = AllPolicyStatements(TemplateJson(homeStack))
            .Single(s => ResourceText(s).Contains(".local:log-stream:*"));
        ResourceText(homeWrite).Should().Contain("AWS::Region");
        ResourceText(homeWrite).Should().Contain("AWS::AccountId");

        var remoteStack = new Stack(new App(), "unfixed-remote");
        var remoteRole = OwnedRole(remoteStack);
        var remoteCentralized = LogGroup.FromLogGroupName(remoteStack, "Centralized", "/custom/logs");
        var remoteConstruct = CreateWebFunction(remoteStack, WebFunctionLogs.Use(remoteCentralized), remoteRole);
        remoteConstruct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        var widenedWrite = AllPolicyStatements(TemplateJson(remoteStack))
            .Single(s => ResourceText(s).Contains(".local:log-stream:*"));
        ResourceText(widenedWrite).Should().Contain(":logs:*:");
    }

    [Fact]
    public void P15_ImportedImmutableRoleWithNonHomeEndpoint_ShouldNotMutateRole()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = Role.FromRoleArn(
            stack,
            "Imported",
            "arn:aws:iam::123456789012:role/caller",
            new FromRoleArnOptions { Mutable = false });
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);
        Action act = () => construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        act.Should().NotThrow();
        ResourcesOfType(TemplateJson(stack), PolicyType).Should().BeEmpty();
    }

    [Fact]
    public void P4b_UseWithoutCompanionAndNonHomeEndpoint_ShouldGrantSingleWidenedCreateGroup()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role);
        construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        var statements = AllPolicyStatements(TemplateJson(stack));
        statements.Count(s => ResourceText(s).Contains(".local:log-stream:*")).Should().Be(1);

        var createGroup = statements.Where(s => Actions(s).Contains("logs:CreateLogGroup")).ToList();
        createGroup.Should().HaveCount(1);
        ResourceArns(createGroup[0]).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:*:123456789012:log-group:/custom/logs.local",
        });
    }

    [Fact]
    public void P16_NonHomeDefaultEndpoint_ShouldWidenStatements()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");

        CreateWebFunction(stack, WebFunctionLogs.Use(centralized), role, endpoint: RemoteEndpoint("default"));

        var statements = AllPolicyStatements(TemplateJson(stack));
        var companionWrite = statements.Where(s => ResourceText(s).Contains(".local:log-stream:*")).ToList();
        companionWrite.Should().HaveCount(1);
        ResourceArns(companionWrite[0]).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:*:123456789012:log-group:/custom/logs.local:log-stream:*",
        });
    }

    [Theory]
    [InlineData(WebFunctionEndpointType.PerRegion)]
    [InlineData(WebFunctionEndpointType.MultiRegion)]
    public void P11b_ResolvingPolicyBeforeAddEndpoint_ShouldStillWiden(WebFunctionEndpointType type)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);

        // Force the companion Region token (embedded in the supplied role's policy) to resolve BEFORE the
        // non-home endpoint exists. A cached lazy value would keep the stack Region here.
        var policy = (Policy)role.Node.FindChild("DefaultPolicy")!;
        stack.Resolve(policy.Document);

        construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = type,
            Regions = ["us-west-2"],
        });

        var statements = AllPolicyStatements(TemplateJson(stack));
        var companionWrite = statements.Where(s => ResourceText(s).Contains(".local:log-stream:*")).ToList();
        companionWrite.Should().HaveCount(1);
        ResourceArns(companionWrite[0]).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:*:123456789012:log-group:/custom/logs.local:log-stream:*",
        });

        statements.Count(s => Actions(s).Contains("logs:CreateLogGroup")).Should().Be(1, "a remote endpoint may create the companion in other Regions");

        var createGroup = statements.Single(s => Actions(s).Contains("logs:CreateLogGroup"));
        ResourceArns(createGroup).Should().BeEquivalentTo(new[]
        {
            "arn:${AWS::Partition}:logs:*:123456789012:log-group:/custom/logs.local",
        });
    }

    // ------------------------------------------------------------------ Warnings

    [Fact]
    public void Wa_W1_ShouldAppearOnlyForUseWithoutCompanion()
    {
        var (_, useOnlyStack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(useOnlyStack, "External", "/custom/logs");
        CreateWebFunction(useOnlyStack, WebFunctionLogs.Use(imported));
        Annotations.FromStack(useOnlyStack)
            .HasWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));

        var (_, createStack) = CdkTestHelper.CreateTestStack();
        CreateWebFunction(createStack, WebFunctionLogs.Create());
        Annotations.FromStack(createStack)
            .HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));

        var (_, useCompanionStack) = CdkTestHelper.CreateTestStack();
        var centralized = LogGroup.FromLogGroupName(useCompanionStack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(useCompanionStack, "Companion", "/custom/logs.local");
        CreateWebFunction(useCompanionStack, WebFunctionLogs.Use(centralized, companion));
        Annotations.FromStack(useCompanionStack)
            .HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));

        var (_, serviceStack) = CdkTestHelper.CreateTestStack();
        CreateWebFunction(serviceStack, WebFunctionLogs.ServiceDefault());
        Annotations.FromStack(serviceStack)
            .HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("use-companion")]
    [InlineData("use-only")]
    [InlineData("service")]
    public void Wb_W2_ShouldAppearForEveryRemoteEndpointWithModeSpecificSentence(string mode)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateWebFunction(stack, LogsFor(mode, stack), endpoint: RemoteEndpoint("default"));
        construct.AddEndpoint("second", RemoteEndpoint("second"));

        Annotations.FromStack(stack).HasWarning("*", Match.StringLikeRegexp(RemoteCompanionWarningId));

        var expected = ModeSentence(mode);
        var endpointMessages = construct.Endpoints
            .SelectMany(endpoint => endpoint.Node.Metadata)
            .Select(entry => entry.Data?.ToString() ?? "")
            .Where(data => data.Contains(RemoteCompanionWarningId, StringComparison.Ordinal))
            .ToList();

        endpointMessages.Should().HaveCount(2);
        endpointMessages.Should().OnlyContain(data => data.Contains(expected, StringComparison.Ordinal));
        // Literal endpoint names read naturally: "Endpoint 'name' (...)".
        endpointMessages.Should().OnlyContain(data => data.StartsWith("Endpoint '", StringComparison.Ordinal));
    }

    [Fact]
    public void Wc_HomeRegionOnly_ShouldNotEmitCompanionWarnings()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateWebFunction(stack, WebFunctionLogs.Create());
        construct.AddEndpoint("second", new WebFunctionEndpointOptions
        {
            EndpointName = "second",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.HomeRegion,
        });

        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(RemoteCompanionWarningId));
        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));
    }

    [Fact]
    public void Wd_Warnings_ShouldUseStableIdsWithAckSuffix()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        var constructMessages = construct.Node.Metadata
            .Select(entry => entry.Data?.ToString() ?? "")
            .ToList();

        constructMessages.Should().Contain(message =>
            message.EndsWith($" [ack: {UnmanagedCompanionWarningId}]", StringComparison.Ordinal));

        var (_, remoteStack) = CdkTestHelper.CreateTestStack();
        var remote = CreateWebFunction(remoteStack, WebFunctionLogs.Create(), endpoint: RemoteEndpoint("default"));

        var endpointMessages = remote.DefaultEndpoint.Node.Metadata
            .Select(entry => entry.Data?.ToString() ?? "")
            .ToList();

        endpointMessages.Should().Contain(message =>
            message.EndsWith($" [ack: {RemoteCompanionWarningId}]", StringComparison.Ordinal));
    }

    [Fact]
    public void We_Warnings_ShouldBeWarningLevelMetadata()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        construct.Node.Metadata
            .Where(entry => (entry.Data?.ToString() ?? "").Contains(UnmanagedCompanionWarningId, StringComparison.Ordinal))
            .Should()
            .OnlyContain(entry => entry.Type == "aws:cdk:warning");
    }

    [Fact]
    public void Wf_AcknowledgeWarning_ShouldRemoveWarningAfterConstruction()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        RuntimeAnnotations.Of(construct).AcknowledgeWarning(UnmanagedCompanionWarningId, "Reviewed.");

        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));
    }

    [Fact]
    public void Wf_AcknowledgeWarning_OnAncestorBeforeConstruction_ShouldSuppressWarning()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        RuntimeAnnotations.Of(stack).AcknowledgeWarning(UnmanagedCompanionWarningId, "Reviewed.");

        var imported = LogGroup.FromLogGroupName(stack, "External", "/custom/logs");
        CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(UnmanagedCompanionWarningId));
    }

    [Fact]
    public void Wg_WarningMessages_ShouldNotContainTokens()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var imported = LogGroup.FromLogGroupName(stack, "External", Fn.ImportValue("centralized-name"));
        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(imported));

        var remoteStack = new Stack(new App(), "remote");
        var remoteRole = OwnedRole(remoteStack);
        var remoteCentralized = LogGroup.FromLogGroupName(remoteStack, "Centralized", "/custom/logs");
        var remote = CreateWebFunction(remoteStack, WebFunctionLogs.Use(remoteCentralized), remoteRole, endpoint: RemoteEndpoint("default"));

        var allMessages = construct.Node.Metadata
            .Concat(remote.DefaultEndpoint.Node.Metadata)
            .Select(entry => entry.Data?.ToString() ?? "")
            .ToList();

        allMessages.Should().NotContain(message => message.Contains("${Token[", StringComparison.Ordinal));
    }

    [Fact]
    public void Wh_TokenizedEndpointName_ShouldUsePlaceholderWithoutTokenText()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var construct = CreateWebFunction(stack, WebFunctionLogs.Create(), endpoint: new WebFunctionEndpointOptions
        {
            EndpointName = Fn.ImportValue("endpoint-name"),
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = WebFunctionEndpointType.PerRegion,
            Regions = ["us-west-2"],
        });

        var messages = construct.DefaultEndpoint.Node.Metadata
            .Select(entry => entry.Data?.ToString() ?? "")
            .Where(data => data.Contains(RemoteCompanionWarningId, StringComparison.Ordinal))
            .ToList();

        messages.Should().HaveCount(1);
        messages[0].Should().StartWith("This endpoint (PerRegion) runs requests in Regions other than the stack Region.");
        messages[0].Should().NotContain("${Token[");
        messages[0].Should().NotContain("Endpoint This endpoint");
    }

    [Fact]
    public void Wf_AcknowledgeWarning_OnEndpoint_ShouldRemoveRemoteWarning()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var construct = CreateWebFunction(stack, WebFunctionLogs.Create(), endpoint: RemoteEndpoint("default"));

        RuntimeAnnotations.Of(construct.DefaultEndpoint).AcknowledgeWarning(RemoteCompanionWarningId, "Reviewed.");

        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(RemoteCompanionWarningId));
    }

    [Fact]
    public void Wf_AcknowledgeWarning_OnStackAfterConstruction_ShouldRemoveRemoteWarning()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        CreateWebFunction(stack, WebFunctionLogs.Create(), endpoint: RemoteEndpoint("default"));

        RuntimeAnnotations.Of(stack).AcknowledgeWarning(RemoteCompanionWarningId, "Reviewed.");

        Annotations.FromStack(stack).HasNoWarning("*", Match.StringLikeRegexp(RemoteCompanionWarningId));
    }

    // ------------------------------------------------------------------ Helpers

    private static void AssertOrderingWidens(WebFunctionEndpointType type)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var role = OwnedRole(stack);
        var centralized = LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs");
        var companion = LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local");

        var construct = CreateWebFunction(stack, WebFunctionLogs.Use(centralized, companion), role);
        construct.AddEndpoint("regional", new WebFunctionEndpointOptions
        {
            EndpointName = "regional",
            AuthType = WebFunctionAuthType.IamAuth,
            EndpointType = type,
            Regions = ["us-west-2"],
        });

        var statements = AllPolicyStatements(TemplateJson(stack));

        var companionWrite = statements.Where(s => ResourceText(s).Contains(".local:log-stream:*")).ToList();
        companionWrite.Should().HaveCount(1);
        ResourceText(companionWrite[0]).Should().Contain(":logs:*:");

        var createGroup = statements.Where(s => Actions(s).Contains("logs:CreateLogGroup")).ToList();
        createGroup.Should().HaveCount(1);
        ResourceText(createGroup[0]).Should().Contain(":logs:*:");
    }

    private static WebFunctionEndpointOptions RemoteEndpoint(string name) => new()
    {
        EndpointName = name,
        AuthType = WebFunctionAuthType.IamAuth,
        EndpointType = WebFunctionEndpointType.PerRegion,
        Regions = ["us-west-2"],
    };

    private static string ModeSentence(string mode) => mode switch
    {
        "create" => "In the stack Region, the centralized group and its companion are managed by this stack.",
        "use-companion" => "In the stack Region, the caller-supplied companion is used.",
        "use-only" => "In the stack Region, the companion is not managed either.",
        _ => "All of this function's log groups, including the centralized group, are service-managed and never expire.",
    };

    private static WebFunctionLogs LogsFor(string mode, Stack stack) => mode switch
    {
        "create" => WebFunctionLogs.Create(),
        "use-companion" => WebFunctionLogs.Use(
            LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs"),
            LogGroup.FromLogGroupName(stack, "Companion", "/custom/logs.local")),
        "use-only" => WebFunctionLogs.Use(LogGroup.FromLogGroupName(stack, "Centralized", "/custom/logs")),
        _ => WebFunctionLogs.ServiceDefault(),
    };

    private static WebFunctionConstruct CreateWebFunction(
        Stack stack,
        WebFunctionLogs logs,
        IRole? role = null,
        string functionName = "test-web-function",
        IWebFunctionEndpointOptions? endpoint = null)
    {
        var artifacts = Bucket.FromBucketName(stack, "Artifacts", "artifacts");
        var props = new WebFunctionConstructProps
        {
            FunctionName = functionName,
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "app.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = endpoint ?? new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
            Logs = logs,
            Role = role,
        };

        return new WebFunctionConstruct(stack, "web", props);
    }

    private static IRole OwnedRole(Stack stack, string id = "CallerRole") =>
        new Role(stack, id, new RoleProps { AssumedBy = new ServicePrincipal("lambda.amazonaws.com") });

    private static JsonElement TemplateJson(Stack stack)
    {
        var template = Template.FromStack(stack);
        var json = JsonSerializer.Serialize(template.ToJSON());
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static List<KeyValuePair<string, JsonElement>> ResourcesOfType(JsonElement template, string type)
    {
        var result = new List<KeyValuePair<string, JsonElement>>();
        foreach (var property in template.GetProperty("Resources").EnumerateObject())
        {
            if (property.Value.TryGetProperty("Type", out var resourceType)
                && resourceType.GetString() == type)
            {
                result.Add(new KeyValuePair<string, JsonElement>(property.Name, property.Value));
            }
        }

        return result;
    }

    private static JsonElement Properties(JsonElement resource) => resource.GetProperty("Properties");

    private static JsonElement FindLogGroup(List<KeyValuePair<string, JsonElement>> groups, string name)
    {
        foreach (var group in groups)
        {
            if (Properties(group.Value).TryGetProperty("LogGroupName", out var logGroupName)
                && logGroupName.ValueKind == JsonValueKind.String
                && logGroupName.GetString() == name)
            {
                return group.Value;
            }
        }

        throw new InvalidOperationException($"No log group named '{name}' was found.");
    }

    private static string LogicalIdOfLogGroup(JsonElement template, string name)
    {
        foreach (var (logicalId, resource) in ResourcesOfType(template, LogGroupType))
        {
            if (Properties(resource).TryGetProperty("LogGroupName", out var logGroupName)
                && logGroupName.ValueKind == JsonValueKind.String
                && logGroupName.GetString() == name)
            {
                return logicalId;
            }
        }

        throw new InvalidOperationException($"No log group named '{name}' was found.");
    }

    private static int RetentionOf(JsonElement logGroup) =>
        Properties(logGroup).GetProperty("RetentionInDays").GetInt32();

    private static string? DeletionPolicyOf(JsonElement resource) =>
        resource.TryGetProperty("DeletionPolicy", out var policy) ? policy.GetString() : null;

    private static JsonElement RevisionOf(JsonElement template, string functionName)
    {
        foreach (var (_, resource) in ResourcesOfType(template, RevisionType))
        {
            if (Properties(resource).TryGetProperty("FunctionName", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() == functionName)
            {
                return resource;
            }
        }

        throw new InvalidOperationException($"No revision for '{functionName}' was found.");
    }

    private static List<string> DependsOnIds(JsonElement resource)
    {
        var result = new List<string>();
        if (!resource.TryGetProperty("DependsOn", out var dependsOn))
            return result;

        if (dependsOn.ValueKind == JsonValueKind.String)
        {
            result.Add(dependsOn.GetString()!);
            return result;
        }

        foreach (var entry in dependsOn.EnumerateArray())
            result.Add(entry.GetString()!);

        return result;
    }

    private static List<JsonElement> AllPolicyStatements(JsonElement template)
    {
        var result = new List<JsonElement>();
        foreach (var (_, resource) in ResourcesOfType(template, PolicyType))
        {
            if (!Properties(resource).TryGetProperty("PolicyDocument", out var document))
                continue;
            if (!document.TryGetProperty("Statement", out var statements))
                continue;

            foreach (var statement in statements.EnumerateArray())
                result.Add(statement);
        }

        return result;
    }

    private static List<string> Actions(JsonElement statement)
    {
        var result = new List<string>();
        if (!statement.TryGetProperty("Action", out var action))
            return result;

        if (action.ValueKind == JsonValueKind.String)
        {
            result.Add(action.GetString()!);
            return result;
        }

        foreach (var entry in action.EnumerateArray())
            result.Add(entry.GetString()!);

        return result;
    }

    private static string ResourceText(JsonElement statement) =>
        statement.TryGetProperty("Resource", out var resource) ? resource.GetRawText() : string.Empty;

    private static List<string> ResourceArns(JsonElement statement)
    {
        var result = new List<string>();
        if (!statement.TryGetProperty("Resource", out var resource))
            return result;

        if (resource.ValueKind == JsonValueKind.String)
        {
            result.Add(resource.GetString()!);
            return result;
        }

        if (resource.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in resource.EnumerateArray())
                result.Add(FlattenArn(entry));
            return result;
        }

        result.Add(FlattenArn(resource));
        return result;
    }

    private static string FlattenArn(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Object => FlattenArnObject(value),
        _ => value.GetRawText(),
    };

    private static string FlattenArnObject(JsonElement value)
    {
        if (value.TryGetProperty("Fn::Join", out var join))
        {
            var builder = new StringBuilder();
            foreach (var part in join[1].EnumerateArray())
                builder.Append(FlattenArn(part));

            return builder.ToString();
        }

        if (value.TryGetProperty("Ref", out var reference))
            return "${" + reference.GetString() + "}";

        if (value.TryGetProperty("Fn::GetAtt", out var getAtt))
            return "${" + string.Join(".", getAtt.EnumerateArray().Select(item => item.GetString())) + "}";

        return value.GetRawText();
    }
}

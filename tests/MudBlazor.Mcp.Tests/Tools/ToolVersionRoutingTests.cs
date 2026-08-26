// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using MudBlazor.Mcp.Models;
using MudBlazor.Mcp.Services;
using MudBlazor.Mcp.Tools;

namespace MudBlazor.Mcp.Tests.Tools;

public class ToolVersionRoutingTests
{
    [Fact]
    public async Task AllTools_UseExplicitVersion()
    {
        var registry = CreateRegistry();
        const string version = "8.13.0";

        var responses = new[]
        {
            await ComponentSearchTools.SearchComponentsAsync(
                registry, NullLogger<ComponentSearchTools>.Instance, "button", "all", 10, CancellationToken.None, version),
            await ComponentSearchTools.GetComponentsByCategoryAsync(
                registry, NullLogger<ComponentSearchTools>.Instance, "Buttons", CancellationToken.None, version),
            await ComponentSearchTools.GetRelatedComponentsAsync(
                registry, NullLogger<ComponentSearchTools>.Instance, "MudButton", "all", CancellationToken.None, version),
            await ComponentListTools.ListComponentsAsync(
                registry, NullLogger<ComponentListTools>.Instance, null, true, CancellationToken.None, version),
            await ComponentListTools.ListCategoriesAsync(
                registry, NullLogger<ComponentListTools>.Instance, CancellationToken.None, version),
            await ComponentExampleTools.GetComponentExamplesAsync(
                registry, NullLogger<ComponentExampleTools>.Instance, "MudButton", 5, null, CancellationToken.None, version),
            await ComponentExampleTools.GetExampleByNameAsync(
                registry, NullLogger<ComponentExampleTools>.Instance, "MudButton", "Basic", CancellationToken.None, version),
            await ComponentExampleTools.ListComponentExamplesAsync(
                registry, NullLogger<ComponentExampleTools>.Instance, "MudButton", CancellationToken.None, version),
            await ComponentDetailTools.GetComponentDetailAsync(
                registry, NullLogger<ComponentDetailTools>.Instance, "MudButton", false, true, CancellationToken.None, version),
            await ComponentDetailTools.GetComponentParametersAsync(
                registry, NullLogger<ComponentDetailTools>.Instance, "MudButton", null, CancellationToken.None, version),
            await ApiReferenceTools.GetApiReferenceAsync(
                registry, NullLogger<ApiReferenceTools>.Instance, "MudButton", "all", CancellationToken.None, version),
            await ApiReferenceTools.GetEnumValuesAsync(
                registry, NullLogger<ApiReferenceTools>.Instance, "Color", CancellationToken.None, version)
        };

        Assert.All(responses, response => Assert.Contains($"v{version}", response));
        Assert.Equal(version, registry.RequestedVersion);
    }

    [Fact]
    public async Task ToolResolver_MapsUnavailableVersionToMcpException()
    {
        var registry = new Mock<IIndexerRegistry>();
        registry
            .Setup(x => x.ResolveAsync("99.0.0", It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new MudBlazorVersionUnavailableException(
                    "99.0.0",
                    "Tag was not found."));

        var exception = await Assert.ThrowsAsync<McpException>(
            () => ApiReferenceTools.GetEnumValuesAsync(
                registry.Object,
                NullLogger<ApiReferenceTools>.Instance,
                "Color",
                CancellationToken.None,
                "99.0.0"));

        Assert.Contains("99.0.0", exception.Message);
        Assert.Contains("unavailable", exception.Message);
    }

    [Fact]
    public async Task ToolResolver_MapsInvalidQueryVersionToMcpException()
    {
        var registry = new Mock<IIndexerRegistry>();
        registry
            .Setup(x => x.ResolveAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ArgumentException(
                    "'invalid' is not a valid version. Expected format: X.Y.Z.",
                    "version"));

        var exception = await Assert.ThrowsAsync<McpException>(
            () => ApiReferenceTools.GetEnumValuesAsync(
                registry.Object,
                NullLogger<ApiReferenceTools>.Instance,
                "Color",
                CancellationToken.None));

        Assert.Contains("not a valid version", exception.Message);
    }

    private static StubIndexerRegistry CreateRegistry()
    {
        var indexer = new Mock<IComponentIndexer>();
        var component = new ComponentInfo(
            Name: "MudButton",
            Namespace: "MudBlazor",
            Summary: "A button component",
            Description: "A button component.",
            Category: "Buttons",
            BaseType: "MudBaseButton",
            Parameters:
            [
                new ComponentParameter(
                    "Color",
                    "Color",
                    "The button color",
                    "Color.Default",
                    false,
                    false,
                    "Appearance")
            ],
            Events: [],
            Methods: [],
            Examples:
            [
                new ComponentExample(
                    "Basic",
                    "Basic button",
                    "<MudButton>Click</MudButton>",
                    null,
                    "Basic.razor",
                    [])
            ],
            RelatedComponents: [],
            DocumentationUrl: null,
            SourceUrl: null);
        var category = new ComponentCategory(
            "Buttons",
            "Buttons",
            "Button components",
            ["MudButton"]);
        var apiReference = new ApiReference(
            "MudButton",
            "MudBlazor",
            "A button component",
            "MudBaseButton",
            [new ApiMember("Color", "Property", "Color", "The button color")]);

        indexer.Setup(x => x.IsIndexed).Returns(true);
        indexer
            .Setup(x => x.SearchComponentsAsync(
                It.IsAny<string>(),
                It.IsAny<SearchFields>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([component]);
        indexer
            .Setup(x => x.GetComponentsByCategoryAsync("Buttons", It.IsAny<CancellationToken>()))
            .ReturnsAsync([component]);
        indexer
            .Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([category]);
        indexer
            .Setup(x => x.GetAllComponentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([component]);
        indexer
            .Setup(x => x.GetComponentAsync("MudButton", It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);
        indexer
            .Setup(x => x.GetRelatedComponentsAsync(
                "MudButton",
                It.IsAny<RelationshipType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([component]);
        indexer
            .Setup(x => x.GetApiReferenceAsync("MudButton", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiReference);

        return new StubIndexerRegistry(indexer.Object);
    }
}

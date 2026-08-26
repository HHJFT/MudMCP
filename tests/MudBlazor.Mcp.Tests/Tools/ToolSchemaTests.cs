// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MudBlazor.Mcp.Services;
using MudBlazor.Mcp.Tools;

namespace MudBlazor.Mcp.Tests.Tools;

public class ToolSchemaTests
{
    [Fact]
    public void AllTools_ExposeOptionalVersionWithoutInfrastructureParameters()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Mock.Of<IIndexerRegistry>())
            .BuildServiceProvider();

        var toolMethods = new[]
            {
                typeof(ApiReferenceTools),
                typeof(ComponentDetailTools),
                typeof(ComponentExampleTools),
                typeof(ComponentListTools),
                typeof(ComponentSearchTools)
            }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToArray();

        Assert.Equal(12, toolMethods.Length);

        foreach (var method in toolMethods)
        {
            var tool = McpServerTool.Create(
                method,
                target: null,
                new McpServerToolCreateOptions { Services = services });
            var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");

            Assert.True(
                properties.TryGetProperty("version", out var versionSchema),
                $"{tool.ProtocolTool.Name} does not expose the version parameter.");
            Assert.Contains(
                versionSchema.GetProperty("type").EnumerateArray(),
                element => element.GetString() == "null");
            Assert.False(properties.TryGetProperty("registry", out _));
            Assert.False(properties.TryGetProperty("logger", out _));
            Assert.False(properties.TryGetProperty("cancellationToken", out _));

            if (tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required))
            {
                Assert.DoesNotContain(
                    required.EnumerateArray(),
                    element => element.GetString() == "version");
            }
        }
    }
}

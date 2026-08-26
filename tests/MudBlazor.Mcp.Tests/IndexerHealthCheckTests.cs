// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests;

public class IndexerHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ResolvesStartupDefaultExplicitly()
    {
        var indexer = new Mock<IComponentIndexer>();
        indexer.SetupGet(x => x.IsIndexed).Returns(false);

        var registry = new Mock<IIndexerRegistry>();
        registry.SetupGet(x => x.DefaultVersion).Returns("9.0.0");
        registry.SetupGet(x => x.LoadedVersions).Returns(Array.Empty<string>());
        registry
            .Setup(x => x.ResolveAsync("9.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ResolvedIndexer(
                    indexer.Object,
                    new VersionContext("9.0.0"),
                    () => ValueTask.CompletedTask));

        var healthCheck = new IndexerHealthCheck(
            registry.Object,
            NullLogger<IndexerHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        registry.Verify(
            x => x.ResolveAsync("9.0.0", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

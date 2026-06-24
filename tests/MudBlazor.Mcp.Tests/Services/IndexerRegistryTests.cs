// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Moq;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests.Services;

public class IndexerRegistryTests
{
    [Fact]
    public async Task ResolveAsync_UsesDefaultVersion_WhenVersionIsNull()
    {
        var factory = new Mock<IVersionedIndexerFactory>();
        var indexer = new Mock<IComponentIndexer>();
        indexer.Setup(x => x.BuildIndexAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        factory.Setup(x => x.Create(It.IsAny<VersionContext>())).Returns(indexer.Object);

        var registry = new IndexerRegistry(factory.Object, new VersionContext("9.0.0", "/tmp/data"));

        var resolved = await registry.ResolveAsync(null, CancellationToken.None);

        Assert.Equal("9.0.0", resolved.VersionContext.Version);
        Assert.Same(indexer.Object, resolved.Indexer);
        factory.Verify(x => x.Create(It.Is<VersionContext>(c => c.Version == "9.0.0")), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_SharesSingleBuildForSameVersion()
    {
        var factory = new Mock<IVersionedIndexerFactory>();
        var indexer = new Mock<IComponentIndexer>();
        var buildCount = 0;
        indexer.Setup(x => x.BuildIndexAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref buildCount);
                return Task.CompletedTask;
            });
        factory.Setup(x => x.Create(It.IsAny<VersionContext>())).Returns(indexer.Object);

        var registry = new IndexerRegistry(factory.Object, new VersionContext("9.0.0", "/tmp/data"));

        var first = await registry.ResolveAsync("8.13.0", CancellationToken.None);
        var second = await registry.ResolveAsync("8.13.0", CancellationToken.None);

        Assert.Same(first.Indexer, second.Indexer);
        Assert.Equal(1, Volatile.Read(ref buildCount));
    }

    [Fact]
    public async Task ResolveAsync_UsesExplicitVersion_WhenProvided()
    {
        var factory = new Mock<IVersionedIndexerFactory>();
        var indexer = new Mock<IComponentIndexer>();
        indexer.Setup(x => x.BuildIndexAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        factory.Setup(x => x.Create(It.IsAny<VersionContext>())).Returns(indexer.Object);

        var registry = new IndexerRegistry(factory.Object, new VersionContext("9.0.0", "/tmp/data"));

        var resolved = await registry.ResolveAsync("8.13.0", CancellationToken.None);

        Assert.Equal("8.13.0", resolved.VersionContext.Version);
        factory.Verify(x => x.Create(It.Is<VersionContext>(c => c.Version == "8.13.0")), Times.Once);
    }
}

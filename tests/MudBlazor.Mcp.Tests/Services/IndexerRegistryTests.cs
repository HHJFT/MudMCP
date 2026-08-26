// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests.Services;

public class IndexerRegistryTests
{
    [Fact]
    public async Task ResolveAsync_UsesDefaultVersion_WhenVersionIsNull()
    {
        var indexer = CreateIndexer();
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        await using var registry = CreateRegistry(factory.Object);

        await using var resolved = await registry.ResolveAsync(null, CancellationToken.None);

        Assert.Equal("9.0.0", resolved.VersionContext.Version);
        Assert.Same(indexer.Object, resolved.Indexer);
        factory.Verify(x => x.Create(It.Is<VersionContext>(c => c.Version == "9.0.0")), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_UsesExplicitVersion_WhenProvided()
    {
        var indexer = CreateIndexer();
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        await using var registry = CreateRegistry(factory.Object);

        await using var resolved = await registry.ResolveAsync("8.13.0", CancellationToken.None);

        Assert.Equal("8.13.0", resolved.VersionContext.Version);
        factory.Verify(x => x.Create(It.Is<VersionContext>(c => c.Version == "8.13.0")), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_UsesHttpQueryVersion_WhenExplicitVersionIsNull()
    {
        var indexer = CreateIndexer();
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?version=8.13.0");
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        await using var registry = CreateRegistry(factory.Object, httpContextAccessor: accessor);

        await using var resolved = await registry.ResolveAsync(null, CancellationToken.None);

        Assert.Equal("8.13.0", resolved.VersionContext.Version);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitVersion_TakesPrecedenceOverHttpQueryVersion()
    {
        var indexer = CreateIndexer();
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?version=8.13.0");
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        await using var registry = CreateRegistry(factory.Object, httpContextAccessor: accessor);

        await using var resolved = await registry.ResolveAsync("9.0.0", CancellationToken.None);

        Assert.Equal("9.0.0", resolved.VersionContext.Version);
    }

    [Fact]
    public async Task ResolveAsync_SharesSingleBuildForConcurrentSameVersionRequests()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexer = CreateIndexer(async _ =>
        {
            buildStarted.TrySetResult();
            await releaseBuild.Task;
        });
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        await using var registry = CreateRegistry(factory.Object);

        var firstTask = registry.ResolveAsync("8.13.0", CancellationToken.None);
        await buildStarted.Task;
        var secondTask = registry.ResolveAsync("8.13.0", CancellationToken.None);

        releaseBuild.TrySetResult();
        await using var first = await firstTask;
        await using var second = await secondTask;

        Assert.Same(first.Indexer, second.Indexer);
        factory.Verify(x => x.Create(It.IsAny<VersionContext>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_SerializesBuildsForDifferentVersions()
    {
        var firstBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var factory = CreateFactory(context =>
        {
            var indexer = context.Version == "8.13.0"
                ? CreateIndexer(async _ =>
                {
                    firstBuildStarted.TrySetResult();
                    await releaseFirstBuild.Task;
                })
                : CreateIndexer(_ =>
                {
                    secondBuildStarted.TrySetResult();
                    return Task.CompletedTask;
                });

            return new VersionedIndexer(indexer.Object);
        });
        await using var registry = CreateRegistry(factory.Object);

        var firstTask = registry.ResolveAsync("8.13.0", CancellationToken.None);
        await firstBuildStarted.Task;
        var secondTask = registry.ResolveAsync("9.0.0", CancellationToken.None);

        Assert.False(secondBuildStarted.Task.IsCompleted);

        releaseFirstBuild.TrySetResult();
        await using var first = await firstTask;
        await using var second = await secondTask;

        Assert.True(secondBuildStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task ResolveAsync_EvictsLeastRecentlyUsedInMemoryVersion()
    {
        var factory = CreateFactory(_ => new VersionedIndexer(CreateIndexer().Object));
        await using var registry = CreateRegistry(factory.Object, maxCachedVersions: 2);

        await using (await registry.ResolveAsync("7.0.0", CancellationToken.None))
        {
        }

        await using (await registry.ResolveAsync("8.0.0", CancellationToken.None))
        {
        }

        await using (await registry.ResolveAsync("7.0.0", CancellationToken.None))
        {
        }

        await using (await registry.ResolveAsync("9.0.0", CancellationToken.None))
        {
        }

        Assert.Equal(new[] { "7.0.0", "9.0.0" }, registry.LoadedVersions);
    }

    [Fact]
    public async Task ResolveAsync_RetriesAfterFailedBuild()
    {
        var attempts = 0;
        var factory = CreateFactory(_ =>
        {
            var indexer = CreateIndexer(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("transient failure");
                }

                return Task.CompletedTask;
            });
            return new VersionedIndexer(indexer.Object);
        });
        await using var registry = CreateRegistry(factory.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ResolveAsync("8.13.0", CancellationToken.None));
        Assert.Empty(registry.LoadedVersions);

        await using var resolved = await registry.ResolveAsync("8.13.0", CancellationToken.None);

        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.Equal(new[] { "8.13.0" }, registry.LoadedVersions);
    }

    [Fact]
    public async Task ResolveAsync_FailedBuildDoesNotEvictLoadedVersion()
    {
        var factory = CreateFactory(context =>
        {
            var indexer = CreateIndexer(_ =>
            {
                if (context.Version == "99.99.99")
                {
                    throw new InvalidOperationException("missing version");
                }

                return Task.CompletedTask;
            });

            return new VersionedIndexer(indexer.Object);
        });
        await using var registry = CreateRegistry(factory.Object, maxCachedVersions: 1);

        await using (await registry.ResolveAsync("9.0.0", CancellationToken.None))
        {
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ResolveAsync("99.99.99", CancellationToken.None));

        Assert.Equal(new[] { "9.0.0" }, registry.LoadedVersions);

        await using (await registry.ResolveAsync("9.0.0", CancellationToken.None))
        {
        }

        factory.Verify(x => x.Create(It.Is<VersionContext>(c => c.Version == "9.0.0")), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_CancelledWaiter_DoesNotCancelSharedBuild()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexer = CreateIndexer(async _ =>
        {
            buildStarted.TrySetResult();
            await releaseBuild.Task;
        });
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        await using var registry = CreateRegistry(factory.Object);

        var sharedBuild = registry.ResolveAsync("8.13.0", CancellationToken.None);
        await buildStarted.Task;

        using var cancellation = new CancellationTokenSource();
        var cancelledWait = registry.ResolveAsync("8.13.0", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait);

        releaseBuild.TrySetResult();
        await using var resolved = await sharedBuild;

        Assert.Equal("8.13.0", resolved.VersionContext.Version);
        factory.Verify(x => x.Create(It.IsAny<VersionContext>()), Times.Once);
    }

    [Fact]
    public async Task LoadedVersions_ExcludesBuildsUntilTheySucceed()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexer = CreateIndexer(async _ =>
        {
            buildStarted.TrySetResult();
            await releaseBuild.Task;
        });
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object));
        await using var registry = CreateRegistry(factory.Object);

        var resolveTask = registry.ResolveAsync("8.13.0", CancellationToken.None);
        await buildStarted.Task;

        Assert.Empty(registry.LoadedVersions);

        releaseBuild.TrySetResult();
        await using var resolved = await resolveTask;

        Assert.Equal(new[] { "8.13.0" }, registry.LoadedVersions);
    }

    [Fact]
    public async Task ResolveAsync_DefersDisposalUntilEvictedLeaseIsReleased()
    {
        var resource = new TrackingAsyncDisposable();
        var factory = CreateFactory(context =>
        {
            object[] ownedResources = context.Version == "8.13.0" ? [resource] : [];
            return new VersionedIndexer(CreateIndexer().Object, ownedResources);
        });
        await using var registry = CreateRegistry(factory.Object, maxCachedVersions: 1);

        var first = await registry.ResolveAsync("8.13.0", CancellationToken.None);
        await using (await registry.ResolveAsync("9.0.0", CancellationToken.None))
        {
        }

        Assert.False(resource.IsDisposed);

        await first.DisposeAsync();

        Assert.True(resource.IsDisposed);
    }

    [Fact]
    public async Task ResolveAsync_RemovesEntryEvictedFromDiskCache()
    {
        var cachedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "8.13.0",
            "9.0.0"
        };
        var cacheManager = new Mock<IVersionCacheManager>();
        cacheManager
            .Setup(x => x.IsVersionCached(It.IsAny<string>()))
            .Returns((string version) => cachedVersions.Contains(version));
        var factory = CreateFactory(_ => new VersionedIndexer(CreateIndexer().Object));
        await using var registry = CreateRegistry(
            factory.Object,
            cacheManager: cacheManager.Object);

        await using (await registry.ResolveAsync("8.13.0", CancellationToken.None))
        {
        }

        cachedVersions.Remove("8.13.0");

        await using (await registry.ResolveAsync("9.0.0", CancellationToken.None))
        {
        }

        Assert.Equal(new[] { "9.0.0" }, registry.LoadedVersions);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForBuildWorkerCleanupBeforeDisposingGate()
    {
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new BlockingAsyncDisposable();
        var indexer = CreateIndexer(async cancellationToken =>
        {
            buildStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        var factory = CreateFactory(_ => new VersionedIndexer(indexer.Object, [resource]));
        var registry = CreateRegistry(factory.Object);

        var resolveTask = registry.ResolveAsync("8.13.0", CancellationToken.None);
        await buildStarted.Task;

        var disposeTask = registry.DisposeAsync().AsTask();
        await resource.DisposalStarted.Task;

        Assert.False(disposeTask.IsCompleted);

        resource.AllowDisposal.TrySetResult();

        await disposeTask;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolveTask);
    }

    private static Mock<IComponentIndexer> CreateIndexer(
        Func<CancellationToken, Task>? build = null)
    {
        var indexer = new Mock<IComponentIndexer>();
        indexer
            .Setup(x => x.BuildIndexAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
                build?.Invoke(token) ?? Task.CompletedTask);
        return indexer;
    }

    private static Mock<IVersionedIndexerFactory> CreateFactory(
        Func<VersionContext, VersionedIndexer> create)
    {
        var factory = new Mock<IVersionedIndexerFactory>();
        factory
            .Setup(x => x.Create(It.IsAny<VersionContext>()))
            .Returns((VersionContext context) => create(context));
        return factory;
    }

    private static IndexerRegistry CreateRegistry(
        IVersionedIndexerFactory factory,
        int maxCachedVersions = 3,
        IHttpContextAccessor? httpContextAccessor = null,
        IVersionCacheManager? cacheManager = null)
    {
        if (cacheManager is null)
        {
            var cacheManagerMock = new Mock<IVersionCacheManager>();
            cacheManagerMock
                .Setup(x => x.IsVersionCached(It.IsAny<string>()))
                .Returns(true);
            cacheManager = cacheManagerMock.Object;
        }

        var options = Options.Create(new MudBlazorOptions
        {
            Repository = new RepositoryOptions
            {
                MaxCachedVersions = maxCachedVersions
            }
        });

        return new IndexerRegistry(
            factory,
            cacheManager,
            options,
            new VersionContext("9.0.0", Path.GetTempPath()),
            NullLogger<IndexerRegistry>.Instance,
            httpContextAccessor);
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingAsyncDisposable : IAsyncDisposable
    {
        public TaskCompletionSource DisposalStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDisposal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            DisposalStarted.TrySetResult();
            await AllowDisposal.Task;
        }
    }
}

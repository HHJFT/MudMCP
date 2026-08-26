// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests.Services;

public class GitRepositoryServiceVersionTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceRepository;
    private readonly string _dataPath;

    public GitRepositoryServiceVersionTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mudmcp-git-test-{Guid.NewGuid():N}");
        _sourceRepository = Path.Combine(_testRoot, "source");
        _dataPath = Path.Combine(_testRoot, "data");

        Directory.CreateDirectory(_sourceRepository);
        Repository.Init(_sourceRepository);

        using var repository = new Repository(_sourceRepository);
        File.WriteAllText(Path.Combine(_sourceRepository, "README.md"), "test repository");
        Commands.Stage(repository, "README.md");
        var signature = new Signature("Mud MCP Tests", "tests@example.com", DateTimeOffset.UtcNow);
        var commit = repository.Commit("Initial commit", signature, signature);
        repository.Tags.Add("v1.0.0", commit);
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WithValidTag_StagesAndPublishesRepository()
    {
        var versionContext = new VersionContext("1.0.0", _dataPath);
        var cacheManager = new VersionCacheManager(_dataPath);
        await using var service = CreateService(versionContext, cacheManager);

        var cloned = await service.EnsureRepositoryAsync(CancellationToken.None);
        var reused = await service.EnsureRepositoryAsync(CancellationToken.None);

        Assert.True(cloned);
        Assert.False(reused);
        Assert.True(service.IsAvailable);
        Assert.True(cacheManager.IsVersionCached("1.0.0"));
        Assert.Empty(Directory.GetDirectories(_dataPath, ".mudblazor-*"));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WithMissingTag_CleansStagingData()
    {
        var versionContext = new VersionContext("2.0.0", _dataPath);
        var cacheManager = new VersionCacheManager(_dataPath);
        await using var service = CreateService(versionContext, cacheManager);

        var exception = await Assert.ThrowsAsync<MudBlazorVersionUnavailableException>(
            () => service.EnsureRepositoryAsync(CancellationToken.None));

        Assert.Equal("2.0.0", exception.Version);
        Assert.False(Directory.Exists(versionContext.VersionDataPath));
        Assert.False(cacheManager.IsVersionCached("2.0.0"));
        Assert.Empty(Directory.GetDirectories(_dataPath, ".mudblazor-*"));
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WithWrongExistingCheckout_DoesNotReuseIt()
    {
        var versionContext = new VersionContext("2.0.0", _dataPath);
        Directory.CreateDirectory(versionContext.VersionDataPath);
        Repository.Clone(_sourceRepository, versionContext.RepoPath);

        var cacheManager = new VersionCacheManager(_dataPath);
        await using var service = CreateService(versionContext, cacheManager);

        Assert.False(service.IsAvailable);

        await Assert.ThrowsAsync<MudBlazorVersionUnavailableException>(
            () => service.EnsureRepositoryAsync(CancellationToken.None));

        Assert.False(Directory.Exists(versionContext.VersionDataPath));
        Assert.False(service.IsAvailable);
    }

    private GitRepositoryService CreateService(
        VersionContext versionContext,
        IVersionCacheManager cacheManager)
    {
        var options = Options.Create(new MudBlazorOptions
        {
            Repository = new MudBlazor.Mcp.Configuration.RepositoryOptions
            {
                Url = _sourceRepository,
                DataPath = _dataPath,
                MaxCachedVersions = 3
            }
        });

        return new GitRepositoryService(
            NullLogger<GitRepositoryService>.Instance,
            options,
            versionContext,
            cacheManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            var root = new DirectoryInfo(_testRoot);
            foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                entry.Attributes = FileAttributes.Normal;
            }

            root.Attributes = FileAttributes.Normal;
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}

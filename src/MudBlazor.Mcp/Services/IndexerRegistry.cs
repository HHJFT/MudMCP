// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Services;

public sealed class IndexerRegistry : IIndexerRegistry
{
    private readonly IVersionedIndexerFactory _factory;
    private readonly VersionContext _defaultVersionContext;
    private readonly ConcurrentDictionary<string, Lazy<Task<ResolvedIndexer>>> _indexers = new(StringComparer.OrdinalIgnoreCase);

    public IndexerRegistry(IVersionedIndexerFactory factory, VersionContext defaultVersionContext)
    {
        _factory = factory;
        _defaultVersionContext = defaultVersionContext;
    }

    public string DefaultVersion => _defaultVersionContext.Version;

    public IReadOnlyCollection<string> LoadedVersions => _indexers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<ResolvedIndexer> ResolveAsync(string? version, CancellationToken cancellationToken = default)
    {
        var effectiveVersion = VersionValidation.ResolveVersion(version, DefaultVersion);
        var lazy = _indexers.GetOrAdd(effectiveVersion, version => new Lazy<Task<ResolvedIndexer>>(() => BuildAsync(version), LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.ConfigureAwait(false);
    }

    private Task<ResolvedIndexer> BuildAsync(string version)
    {
        var versionContext = new VersionContext(version, _defaultVersionContext.DataPath);
        var indexer = _factory.Create(versionContext);
        return BuildIndexerAsync(indexer, versionContext);
    }

    private static async Task<ResolvedIndexer> BuildIndexerAsync(IComponentIndexer indexer, VersionContext versionContext)
    {
        await indexer.BuildIndexAsync().ConfigureAwait(false);
        return new ResolvedIndexer(indexer, versionContext);
    }
}

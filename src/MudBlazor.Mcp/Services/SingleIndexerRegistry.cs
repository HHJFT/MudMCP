// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Services;

public sealed class SingleIndexerRegistry : IIndexerRegistry
{
    private readonly IComponentIndexer _indexer;
    private readonly VersionContext _versionContext;

    public SingleIndexerRegistry(IComponentIndexer indexer, VersionContext versionContext)
    {
        _indexer = indexer;
        _versionContext = versionContext;
    }

    public string DefaultVersion => _versionContext.Version;

    public IReadOnlyCollection<string> LoadedVersions => [_versionContext.Version];

    public Task<ResolvedIndexer> ResolveAsync(string? version, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ResolvedIndexer(_indexer, _versionContext));
    }
}

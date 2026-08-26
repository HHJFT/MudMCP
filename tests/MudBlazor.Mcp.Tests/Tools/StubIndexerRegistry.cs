// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tests.Tools;

internal sealed class StubIndexerRegistry : IIndexerRegistry
{
    private readonly IComponentIndexer _indexer;

    public StubIndexerRegistry(IComponentIndexer indexer, string defaultVersion = "9.0.0")
    {
        _indexer = indexer;
        DefaultVersion = defaultVersion;
    }

    public string DefaultVersion { get; }

    public IReadOnlyCollection<string> LoadedVersions => [DefaultVersion];

    public string? RequestedVersion { get; private set; }

    public Task<ResolvedIndexer> ResolveAsync(
        string? version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedVersion = version;

        var resolvedVersion = VersionValidation.ResolveVersion(version, DefaultVersion);
        return Task.FromResult(
            new ResolvedIndexer(
                _indexer,
                new VersionContext(resolvedVersion)));
    }
}

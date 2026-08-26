// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Services;

public interface IIndexerRegistry
{
    string DefaultVersion { get; }
    IReadOnlyCollection<string> LoadedVersions { get; }
    Task<ResolvedIndexer> ResolveAsync(string? version, CancellationToken cancellationToken = default);
}

public sealed class ResolvedIndexer : IAsyncDisposable
{
    private Func<ValueTask>? _release;

    public ResolvedIndexer(
        IComponentIndexer indexer,
        VersionContext versionContext,
        Func<ValueTask>? release = null)
    {
        Indexer = indexer;
        VersionContext = versionContext;
        _release = release;
    }

    public IComponentIndexer Indexer { get; }

    public VersionContext VersionContext { get; }

    public ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        return release is null ? ValueTask.CompletedTask : release();
    }
}

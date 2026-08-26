// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Services;

public interface IVersionedIndexerFactory
{
    VersionedIndexer Create(VersionContext versionContext);
}

public sealed class VersionedIndexer : IAsyncDisposable
{
    private readonly object[] _ownedResources;
    private int _disposed;

    public VersionedIndexer(IComponentIndexer indexer, params object[] ownedResources)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        Indexer = indexer;
        _ownedResources = ownedResources;
    }

    public IComponentIndexer Indexer { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? firstException = null;

        for (var i = _ownedResources.Length - 1; i >= 0; i--)
        {
            try
            {
                switch (_ownedResources[i])
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                firstException ??= ex;
            }
        }

        if (firstException is not null)
        {
            throw firstException;
        }
    }
}

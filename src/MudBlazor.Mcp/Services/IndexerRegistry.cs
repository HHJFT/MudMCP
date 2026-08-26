// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Services;

public sealed class IndexerRegistry : IIndexerRegistry, IAsyncDisposable
{
    private readonly IVersionedIndexerFactory _factory;
    private readonly IVersionCacheManager _cacheManager;
    private readonly VersionContext _defaultVersionContext;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<IndexerRegistry> _logger;
    private readonly int _maxCachedVersions;
    private readonly object _entriesLock = new();
    private readonly Dictionary<string, RegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private long _accessSequence;
    private int _disposed;

    public IndexerRegistry(
        IVersionedIndexerFactory factory,
        IVersionCacheManager cacheManager,
        IOptions<MudBlazorOptions> options,
        VersionContext defaultVersionContext,
        ILogger<IndexerRegistry> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(defaultVersionContext);
        ArgumentNullException.ThrowIfNull(logger);

        _factory = factory;
        _cacheManager = cacheManager;
        _defaultVersionContext = defaultVersionContext;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _maxCachedVersions = options.Value.Repository.MaxCachedVersions;

        if (_maxCachedVersions < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _maxCachedVersions,
                "MudBlazor:Repository:MaxCachedVersions must be at least 1.");
        }
    }

    public string DefaultVersion => _defaultVersionContext.Version;

    public IReadOnlyCollection<string> LoadedVersions
    {
        get
        {
            lock (_entriesLock)
            {
                return _entries
                    .Where(pair => pair.Value.BuildTask.IsCompletedSuccessfully)
                    .Select(pair => pair.Key)
                    .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public async Task<ResolvedIndexer> ResolveAsync(string? version, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var effectiveVersion = VersionValidation.ResolveVersion(GetRequestedVersion(version), DefaultVersion);
        var entry = await AcquireEntryAsync(effectiveVersion, cancellationToken).ConfigureAwait(false);

        try
        {
            var versionedIndexer = await entry.BuildTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            entry.Touch(Interlocked.Increment(ref _accessSequence));
            _cacheManager.TouchVersion(effectiveVersion);

            return new ResolvedIndexer(
                versionedIndexer.Indexer,
                entry.VersionContext,
                () => ReleaseEntryAsync(entry));
        }
        catch
        {
            await ReleaseEntryAsync(entry).ConfigureAwait(false);
            throw;
        }
    }

    private string? GetRequestedVersion(string? explicitVersion)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion))
        {
            return explicitVersion;
        }

        var queryVersion = _httpContextAccessor?.HttpContext?.Request.Query["version"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(queryVersion) ? null : queryVersion;
    }

    private async Task<RegistryEntry> AcquireEntryAsync(string version, CancellationToken cancellationToken)
    {
        lock (_entriesLock)
        {
            if (_entries.TryGetValue(version, out var existing))
            {
                existing.AddReference(Interlocked.Increment(ref _accessSequence));
                return existing;
            }
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _buildGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        var buildOwnsGate = false;

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            RegistryEntry entry;

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(version, out var existing))
                {
                    existing.AddReference(Interlocked.Increment(ref _accessSequence));
                    return existing;
                }

                var versionContext = new VersionContext(version, _defaultVersionContext.DataPath);
                entry = new RegistryEntry(
                    versionContext,
                    Interlocked.Increment(ref _accessSequence));
                entry.AddReference(entry.LastAccess);
                _entries.Add(version, entry);
            }

            buildOwnsGate = true;
            _ = BuildEntryAsync(entry);

            return entry;
        }
        finally
        {
            if (!buildOwnsGate)
            {
                _buildGate.Release();
            }
        }
    }

    private async Task BuildEntryAsync(RegistryEntry entry)
    {
        VersionedIndexer? versionedIndexer = null;

        try
        {
            versionedIndexer = _factory.Create(entry.VersionContext);
            await versionedIndexer.Indexer.BuildIndexAsync(_shutdown.Token).ConfigureAwait(false);
            _cacheManager.TouchVersion(entry.VersionContext.Version);

            var evictedIndexers = ReconcileWithDiskCacheAndCapacity(entry.VersionContext.Version);
            var indexerToDispose = entry.Complete(versionedIndexer);
            versionedIndexer = null;

            foreach (var evictedIndexer in evictedIndexers)
            {
                await DisposeIndexerAsync(evictedIndexer).ConfigureAwait(false);
            }

            if (indexerToDispose is not null)
            {
                await DisposeIndexerAsync(indexerToDispose).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            RemoveFailedEntry(entry);
            entry.Cancel(_shutdown.Token);
        }
        catch (Exception ex)
        {
            RemoveFailedEntry(entry);
            entry.Fail(ex);
        }
        finally
        {
            if (versionedIndexer is not null)
            {
                await DisposeIndexerAsync(versionedIndexer).ConfigureAwait(false);
            }

            _buildGate.Release();
        }
    }

    private IReadOnlyList<VersionedIndexer> ReconcileWithDiskCacheAndCapacity(string currentVersion)
    {
        var evictedIndexers = new List<VersionedIndexer>();

        lock (_entriesLock)
        {
            var staleVersions = _entries
                .Where(pair =>
                    !pair.Key.Equals(currentVersion, StringComparison.OrdinalIgnoreCase)
                    && pair.Value.BuildTask.IsCompletedSuccessfully
                    && !_cacheManager.IsVersionCached(pair.Key))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var staleVersion in staleVersions)
            {
                var staleEntry = _entries[staleVersion];
                _entries.Remove(staleVersion);

                var indexer = staleEntry.MarkEvicted();
                if (indexer is not null)
                {
                    evictedIndexers.Add(indexer);
                }

                _logger.LogInformation(
                    "Removed in-memory index for MudBlazor v{Version} after disk cache eviction",
                    staleVersion);
            }

            while (_entries.Count > _maxCachedVersions)
            {
                var leastRecentlyUsed = _entries
                    .Where(pair => !pair.Key.Equals(currentVersion, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(pair => pair.Value.LastAccess)
                    .First();

                _entries.Remove(leastRecentlyUsed.Key);

                var indexer = leastRecentlyUsed.Value.MarkEvicted();
                if (indexer is not null)
                {
                    evictedIndexers.Add(indexer);
                }

                _logger.LogInformation(
                    "Evicted in-memory index for MudBlazor v{Version} (LRU)",
                    leastRecentlyUsed.Key);
            }
        }

        return evictedIndexers;
    }

    private void RemoveFailedEntry(RegistryEntry entry)
    {
        lock (_entriesLock)
        {
            if (_entries.TryGetValue(entry.VersionContext.Version, out var current)
                && ReferenceEquals(current, entry))
            {
                _entries.Remove(entry.VersionContext.Version);
                entry.MarkEvicted();
            }
        }
    }

    private async ValueTask DisposeIndexerAsync(VersionedIndexer indexer)
    {
        try
        {
            await indexer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose a versioned indexer");
        }
    }

    private async ValueTask ReleaseEntryAsync(RegistryEntry entry)
    {
        var indexer = entry.Release();
        if (indexer is not null)
        {
            await DisposeIndexerAsync(indexer).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);

        RegistryEntry[] entries;
        var indexersToDispose = new List<VersionedIndexer>();

        lock (_entriesLock)
        {
            entries = _entries.Values.ToArray();
            _entries.Clear();

            foreach (var entry in entries)
            {
                var indexer = entry.MarkEvicted();
                if (indexer is not null)
                {
                    indexersToDispose.Add(indexer);
                }
            }
        }

        foreach (var entry in entries)
        {
            try
            {
                await entry.BuildTask.ConfigureAwait(false);
            }
            catch
            {
                // Build failures are already surfaced to callers and removed from the registry.
            }
        }

        foreach (var indexer in indexersToDispose)
        {
            await DisposeIndexerAsync(indexer).ConfigureAwait(false);
        }

        await _buildGate.WaitAsync().ConfigureAwait(false);
        _buildGate.Dispose();
        _shutdown.Dispose();
    }

    private sealed class RegistryEntry
    {
        private readonly object _stateLock = new();
        private readonly TaskCompletionSource<VersionedIndexer> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private VersionedIndexer? _versionedIndexer;
        private int _referenceCount;
        private bool _evicted;
        private bool _disposeStarted;
        private long _lastAccess;

        public RegistryEntry(VersionContext versionContext, long lastAccess)
        {
            VersionContext = versionContext;
            _lastAccess = lastAccess;
        }

        public VersionContext VersionContext { get; }

        public Task<VersionedIndexer> BuildTask => _completion.Task;

        public long LastAccess => Interlocked.Read(ref _lastAccess);

        public void AddReference(long accessSequence)
        {
            lock (_stateLock)
            {
                if (_evicted)
                {
                    throw new InvalidOperationException(
                        $"Cannot acquire evicted MudBlazor version {VersionContext.Version}.");
                }

                _referenceCount++;
                Interlocked.Exchange(ref _lastAccess, accessSequence);
            }
        }

        public void Touch(long accessSequence)
            => Interlocked.Exchange(ref _lastAccess, accessSequence);

        public VersionedIndexer? Complete(VersionedIndexer versionedIndexer)
        {
            VersionedIndexer? indexerToDispose;

            lock (_stateLock)
            {
                _versionedIndexer = versionedIndexer;
                indexerToDispose = TakeIndexerForDisposalIfReady();
            }

            _completion.TrySetResult(versionedIndexer);
            return indexerToDispose;
        }

        public void Fail(Exception exception)
            => _completion.TrySetException(exception);

        public void Cancel(CancellationToken cancellationToken)
            => _completion.TrySetCanceled(cancellationToken);

        public VersionedIndexer? MarkEvicted()
        {
            lock (_stateLock)
            {
                _evicted = true;
                return TakeIndexerForDisposalIfReady();
            }
        }

        public VersionedIndexer? Release()
        {
            lock (_stateLock)
            {
                if (_referenceCount == 0)
                {
                    return null;
                }

                _referenceCount--;
                return TakeIndexerForDisposalIfReady();
            }
        }

        private VersionedIndexer? TakeIndexerForDisposalIfReady()
        {
            if (!_evicted
                || _referenceCount != 0
                || _versionedIndexer is null
                || _disposeStarted)
            {
                return null;
            }

            _disposeStarted = true;
            return _versionedIndexer;
        }
    }
}

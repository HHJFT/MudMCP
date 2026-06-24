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

public sealed record ResolvedIndexer(IComponentIndexer Indexer, VersionContext VersionContext);

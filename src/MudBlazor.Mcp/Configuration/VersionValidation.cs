// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace MudBlazor.Mcp.Configuration;

public static partial class VersionValidation
{
    private static readonly Regex VersionPattern = VersionRegex();

    public static bool IsValidVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return VersionPattern.IsMatch(version.Trim());
    }

    public static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("MudBlazor version cannot be null or empty.", nameof(version));
        }

        var normalized = version.Trim();
        if (!IsValidVersion(normalized))
        {
            throw new ArgumentException($"'{version}' is not a valid version. Expected format: X.Y.Z or X.Y.Z-prerelease (e.g., 9.0.0 or 9.0.0-preview.1)", nameof(version));
        }

        return normalized;
    }

    public static string ResolveVersion(string? requestedVersion, string defaultVersion)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return NormalizeVersion(defaultVersion);
        }

        return NormalizeVersion(requestedVersion);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex VersionRegex();
}

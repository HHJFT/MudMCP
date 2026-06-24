// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using MudBlazor.Mcp.Configuration;

namespace MudBlazor.Mcp.Tests.Services;

public class VersionValidationTests
{
    [Theory]
    [InlineData("9.0.0", true)]
    [InlineData("9.0.0-preview.1", true)]
    [InlineData("9.0", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidVersion_ReturnsExpected(string? version, bool expected)
    {
        Assert.Equal(expected, VersionValidation.IsValidVersion(version));
    }

    [Fact]
    public void NormalizeVersion_TrimsAndPreservesValidVersion()
    {
        Assert.Equal("9.0.0", VersionValidation.NormalizeVersion(" 9.0.0 "));
    }

    [Fact]
    public void NormalizeVersion_ThrowsForInvalidVersion()
    {
        Assert.Throws<ArgumentException>(() => VersionValidation.NormalizeVersion("latest"));
    }
}

// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Tests.Services;

public class CategoryMapperTests
{
    [Fact]
    public async Task InitializeAsync_IsSafeForConcurrentCallers()
    {
        var mapper = new CategoryMapper(NullLogger<CategoryMapper>.Instance);

        await Task.WhenAll(
            Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => mapper.InitializeAsync(string.Empty)))
                .ToArray());

        var categories = mapper.GetCategories();

        Assert.NotEmpty(categories);
        Assert.Equal(
            categories.Count,
            categories.Select(category => category.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

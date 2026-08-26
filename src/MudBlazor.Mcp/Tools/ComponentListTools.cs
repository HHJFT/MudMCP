// Copyright (c) 2025 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MudBlazor.Mcp.Services;

namespace MudBlazor.Mcp.Tools;

/// <summary>
/// MCP tools for listing and browsing MudBlazor components.
/// </summary>
[McpServerToolType]
public sealed class ComponentListTools
{
    [McpServerTool(Name = "list_components")]
    [Description("Lists all available MudBlazor components. Optionally filter by category and include additional details. Results use the selected MudBlazor version. Set version to the MudBlazor PackageReference from the project's .csproj, or omit it to use the server default.")]
    public static Task<string> ListComponentsAsync(
        IIndexerRegistry registry,
        ILogger<ComponentListTools> logger,
        [Description("Optional category to filter by (e.g., 'Buttons', 'Form Inputs', 'Navigation')")]
        string? category = null,
        [Description("Include parameter counts and brief descriptions (default: true)")]
        bool? includeDetails = null,
        CancellationToken cancellationToken = default,
        [Description("Optional MudBlazor version to serve (e.g., '8.13.0'). Omit to use the server default version.")]
        string? version = null)
        => ListComponentsCoreAsync(registry, logger, category, includeDetails, cancellationToken, version);

    private static async Task<string> ListComponentsCoreAsync(
        IIndexerRegistry registry,
        ILogger<ComponentListTools> logger,
        [Description("Optional category to filter by (e.g., 'Buttons', 'Form Inputs', 'Navigation')")] 
        string? category = null,
        [Description("Include parameter counts and brief descriptions (default: true)")]
        bool? includeDetails = null,
        CancellationToken cancellationToken = default,
        [Description("Optional MudBlazor version to serve (e.g., '8.13.0'). Omit to use the server default version.")]
        string? version = null)
    {
        await using var resolved = await ToolValidation.ResolveIndexerAsync(registry, version, cancellationToken);
        var indexer = resolved.Indexer;
        var responseVersionContext = resolved.VersionContext;

        // Apply default value if not provided (MCP clients may send null for optional parameters)
        var effectiveIncludeDetails = includeDetails ?? true;

        logger.LogDebug("Listing components with category filter: {Category}, includeDetails: {IncludeDetails}",
            category ?? "none", effectiveIncludeDetails);

        if (!indexer.IsIndexed)
        {
            logger.LogWarning("Index not ready when listing components");
            ToolValidation.ThrowIndexNotReady();
        }

        var components = string.IsNullOrWhiteSpace(category)
            ? await indexer.GetAllComponentsAsync(cancellationToken)
            : await indexer.GetComponentsByCategoryAsync(category, cancellationToken);

        logger.LogDebug("Found {Count} components", components.Count);

        if (components.Count == 0)
        {
            if (category is not null)
            {
                var categories = await indexer.GetCategoriesAsync(cancellationToken);
                ToolValidation.ThrowCategoryNotFound(category, categories.Select(c => c.Name));
            }
            return "No components found. The index may not have been built yet.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# MudBlazor Components v{responseVersionContext.Version} ({components.Count} total)");
        sb.AppendLine();

        if (category is not null)
        {
            sb.AppendLine($"**Category:** {category}");
            sb.AppendLine();
        }

        // Group by category if not filtering
        var grouped = category is null 
            ? components.GroupBy(c => c.Category ?? "Uncategorized").OrderBy(g => g.Key)
            : components.GroupBy(c => category).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var component in group.OrderBy(c => c.Name))
            {
                if (effectiveIncludeDetails)
                {
                    sb.AppendLine($"- **{component.Name}**: {TruncateText(component.Summary, 80)}");
                    sb.AppendLine($"  - Parameters: {component.Parameters.Count}, Events: {component.Events.Count}, Examples: {component.Examples.Count}");
                }
                else
                {
                    sb.AppendLine($"- {component.Name}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*Use `get_component_detail` for detailed information about a specific component.*");

        return sb.ToString();
    }

    [McpServerTool(Name = "list_categories")]
    [Description("Lists all MudBlazor component categories with descriptions and component counts. Results use the selected MudBlazor version. Set version to the MudBlazor PackageReference from the project's .csproj, or omit it to use the server default.")]
    public static Task<string> ListCategoriesAsync(
        IIndexerRegistry registry,
        ILogger<ComponentListTools> logger,
        CancellationToken cancellationToken = default,
        [Description("Optional MudBlazor version to serve (e.g., '8.13.0'). Omit to use the server default version.")]
        string? version = null)
        => ListCategoriesCoreAsync(registry, logger, cancellationToken, version);

    private static async Task<string> ListCategoriesCoreAsync(
        IIndexerRegistry registry,
        ILogger<ComponentListTools> logger,
        CancellationToken cancellationToken = default,
        [Description("Optional MudBlazor version to serve (e.g., '8.13.0'). Omit to use the server default version.")]
        string? version = null)
    {
        await using var resolved = await ToolValidation.ResolveIndexerAsync(registry, version, cancellationToken);
        var indexer = resolved.Indexer;
        var responseVersionContext = resolved.VersionContext;

        logger.LogDebug("Listing all component categories");

        if (!indexer.IsIndexed)
        {
            logger.LogWarning("Index not ready when listing categories");
            ToolValidation.ThrowIndexNotReady();
        }

        var categories = await indexer.GetCategoriesAsync(cancellationToken);
        var allComponents = await indexer.GetAllComponentsAsync(cancellationToken);

        logger.LogDebug("Found {CategoryCount} categories with {ComponentCount} total components",
            categories.Count, allComponents.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"# MudBlazor Component Categories (v{responseVersionContext.Version})");
        sb.AppendLine();

        foreach (var category in categories)
        {
            var componentCount = allComponents.Count(c => c.Category == category.Name);
            
            sb.AppendLine($"## {category.Title}");
            if (!string.IsNullOrEmpty(category.Description))
            {
                sb.AppendLine($"*{category.Description}*");
            }
            sb.AppendLine($"- **Components:** {componentCount}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*Use `list_components` with a category filter to see components in a specific category.*");

        return sb.ToString();
    }

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "No description available";

        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}

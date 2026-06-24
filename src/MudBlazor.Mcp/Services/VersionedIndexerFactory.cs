// Copyright (c) 2026 Mud MCP Contributors
// Licensed under the GNU General Public License v2.0. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Mcp.Configuration;
using MudBlazor.Mcp.Services.Parsing;

namespace MudBlazor.Mcp.Services;

public sealed class VersionedIndexerFactory : IVersionedIndexerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVersionCacheManager _cacheManager;
    private readonly IOptions<MudBlazorOptions> _options;
    private readonly XmlDocParser _xmlDocParser;
    private readonly RazorDocParser _razorDocParser;
    private readonly ExampleExtractor _exampleExtractor;
    private readonly CategoryMapper _categoryMapper;
    private readonly ILoggerFactory _loggerFactory;

    public VersionedIndexerFactory(
        IServiceProvider serviceProvider,
        IVersionCacheManager cacheManager,
        IOptions<MudBlazorOptions> options,
        XmlDocParser xmlDocParser,
        RazorDocParser razorDocParser,
        ExampleExtractor exampleExtractor,
        CategoryMapper categoryMapper,
        ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _cacheManager = cacheManager;
        _options = options;
        _xmlDocParser = xmlDocParser;
        _razorDocParser = razorDocParser;
        _exampleExtractor = exampleExtractor;
        _categoryMapper = categoryMapper;
        _loggerFactory = loggerFactory;
    }

    public IComponentIndexer Create(VersionContext versionContext)
    {
        ArgumentNullException.ThrowIfNull(versionContext);

        var logger = _loggerFactory.CreateLogger<ComponentIndexer>();
        var documentationCache = new DocumentationCache(
            _serviceProvider.GetRequiredService<IMemoryCache>(),
            _serviceProvider.GetRequiredService<IOptions<CacheOptions>>(),
            _loggerFactory.CreateLogger<DocumentationCache>());

        var gitRepositoryService = new GitRepositoryService(
            _loggerFactory.CreateLogger<GitRepositoryService>(),
            _serviceProvider.GetRequiredService<IOptions<MudBlazorOptions>>(),
            versionContext,
            _cacheManager);

        return new ComponentIndexer(
            gitRepositoryService,
            documentationCache,
            _xmlDocParser,
            _razorDocParser,
            _exampleExtractor,
            _categoryMapper,
            versionContext,
            _options,
            logger);
    }
}

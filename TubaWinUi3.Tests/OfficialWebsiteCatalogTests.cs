using System;
using System.Linq;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class OfficialWebsiteCatalogTests
{
    public static IEnumerable<OfficialWebsite> AllSites =>
        OfficialWebsiteCatalog.GetCategories().SelectMany(c => c.Sites);

    [Fact]
    public void Catalog_HasMultipleCategories()
    {
        var categories = OfficialWebsiteCatalog.GetCategories();
        Assert.NotEmpty(categories);
        Assert.True(categories.Count >= 5, $"分类过少: {categories.Count}");
    }

    [Fact]
    public void AllCategories_AreNotEmpty()
    {
        foreach (var category in OfficialWebsiteCatalog.GetCategories())
        {
            Assert.NotEmpty(category.Sites);
            Assert.False(string.IsNullOrWhiteSpace(category.Name));
        }
    }

    [Fact]
    public void AllSites_HaveValidAbsoluteHttpUris()
    {
        foreach (var site in AllSites)
        {
            Assert.True(Uri.TryCreate(site.Url, UriKind.Absolute, out var uri),
                $"{site.Name} 的 URL 非法: {site.Url}");
            Assert.True(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps,
                $"{site.Name} 必须使用 http/https: {site.Url}");
        }
    }

    [Fact]
    public void AllSites_HaveNonEmptyNames()
    {
        foreach (var site in AllSites)
        {
            Assert.False(string.IsNullOrWhiteSpace(site.Name), $"存在空名的站点: {site.Url}");
        }
    }

    [Fact]
    public void AllSites_HaveFaviconUrl()
    {
        foreach (var site in AllSites)
        {
            Assert.True(Uri.TryCreate(site.FaviconUrl, UriKind.Absolute, out var favicon),
                $"{site.Name} 的图标地址非法: {site.FaviconUrl}");
            Assert.True(favicon.Scheme == Uri.UriSchemeHttps,
                $"{site.Name} 的图标必须使用 https: {site.FaviconUrl}");
        }
    }

    [Fact]
    public void SiteNames_AreUnique()
    {
        var duplicates = AllSites
            .GroupBy(s => s.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Tool_IsRegistered_InDefaultRegistry()
    {
        var tool = new OfficialWebsitesTool();
        Assert.Equal("official-websites", tool.Id);
        Assert.Equal("常用官网", tool.Name);
        Assert.NotEmpty(tool.Glyph);
        Assert.NotEmpty(tool.Category);
        Assert.Equal(BuiltinToolKind.InstantAction, tool.Kind);
        Assert.Equal(Task.CompletedTask, tool.ExecuteAsync(new BuiltinToolContext()));
    }
}
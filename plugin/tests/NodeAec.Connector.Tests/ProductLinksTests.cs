using NodeAec.Connector.Client;
using NodeAec.Connector.Config;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ProductLinksTests
{
    [Fact]
    public void BuildProductUrl_WithSlug_ReturnsProductPage()
    {
        string url = ProductLinks.BuildProductUrl("revit-automator");

        Assert.Equal($"{ConnectorConfig.CatalogUrl}/revit-automator", url);
    }

    [Fact]
    public void BuildProductUrl_WithEmptySlug_ReturnsCatalog()
    {
        Assert.Equal(ConnectorConfig.CatalogUrl, ProductLinks.BuildProductUrl(null));
        Assert.Equal(ConnectorConfig.CatalogUrl, ProductLinks.BuildProductUrl("   "));
    }

    [Fact]
    public void BuildProductUrl_TrimsAndEncodesSlug()
    {
        string url = ProductLinks.BuildProductUrl("  meu plugin  ");

        Assert.Equal($"{ConnectorConfig.CatalogUrl}/meu%20plugin", url);
    }
}

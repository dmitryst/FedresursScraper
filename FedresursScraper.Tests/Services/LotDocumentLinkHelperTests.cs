using FedresursScraper.Services;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class LotDocumentLinkHelperTests
{
    [Fact]
    public void IsFedresursDocumentUrl_DetectsBackendUrl()
    {
        var url = "https://fedresurs.ru/backend/bankruptcy-message-docs/70cf41a2-f183-4214-9684-919219baec82";

        Assert.True(LotDocumentLinkHelper.IsFedresursDocumentUrl(url));
    }

    [Fact]
    public void BuildDownloadApiPath_ReturnsExpectedPath()
    {
        var docId = Guid.Parse("70cf41a2-f183-4214-9684-919219baec82");

        Assert.Equal(
            "/api/lots/178797/documents/70cf41a2-f183-4214-9684-919219baec82/download",
            LotDocumentLinkHelper.BuildDownloadApiPath(178797, docId));
    }
}

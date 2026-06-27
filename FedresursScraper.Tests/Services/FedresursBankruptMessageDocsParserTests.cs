using FedresursScraper.Services;
using Xunit;

namespace FedresursScraper.Tests.Services;

public class FedresursBankruptMessageDocsParserTests
{
    [Fact]
    public void ParseAttachmentsFromApiJson_ReturnsThreeDocuments()
    {
        const string json = """
            {
              "guid": "0fe9818a-c007-4911-9f5e-433d0429f432",
              "docs": [
                {
                  "guid": "70cf41a2-f183-4214-9684-919219baec82",
                  "name": "Состав лота.docx",
                  "size": 287736,
                  "isDangerous": false
                },
                {
                  "guid": "6eec4c43-921e-4d85-82f4-9ef892e6355d",
                  "name": "Договор о задатке Агора (МТС) (1).rar",
                  "size": 31562,
                  "isDangerous": false
                },
                {
                  "guid": "07c420f8-af28-4693-b837-e9f8ca248f9f",
                  "name": "Проект ДКП движимое (2).docx",
                  "size": 20396,
                  "isDangerous": false
                }
              ]
            }
            """;

        var attachments = FedresursBankruptMessageDocsParser.ParseAttachmentsFromApiJson(json);

        Assert.Equal(3, attachments.Count);
        Assert.Equal("Состав лота.docx", attachments[0].Title);
        Assert.Equal(
            "https://fedresurs.ru/backend/bankruptcy-message-docs/70cf41a2-f183-4214-9684-919219baec82",
            attachments[0].Url);
        Assert.Equal(".docx", attachments[0].Extension);
        Assert.Equal(".rar", attachments[1].Extension);
    }

    [Fact]
    public void ParseAttachmentsFromApiJson_EmptyDocs_ReturnsEmpty()
    {
        const string json = """{"guid":"0fe9818a-c007-4911-9f5e-433d0429f432","docs":[]}""";

        var attachments = FedresursBankruptMessageDocsParser.ParseAttachmentsFromApiJson(json);

        Assert.Empty(attachments);
    }
}

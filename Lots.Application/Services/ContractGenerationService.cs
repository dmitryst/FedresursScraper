using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lots.Application.Services;

public class ContractGenerationService
{
    private readonly LotsDbContext _dbContext;

    public ContractGenerationService(LotsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, Guid lotId)
    {
        return await _dbContext.UserLotContractPermissions
            .AnyAsync(p => p.UserId == userId && p.LotId == lotId);
    }

    public async Task<byte[]> GenerateContractAsync(Guid userId, Guid lotId, DTOs.ContractGenerationRequest request, string templatePath)
    {
        var hasPermission = await HasPermissionAsync(userId, lotId);
        if (!hasPermission)
        {
            throw new UnauthorizedAccessException("У пользователя нет прав на формирование договора для данного лота.");
        }

        var lot = await _dbContext.Lots
            .Include(l => l.Bidding)
            .Include(l => l.CadastralNumbers)
            .FirstOrDefaultAsync(l => l.Id == lotId);

        if (lot == null)
        {
            throw new KeyNotFoundException("Лот не найден.");
        }

        // Загружаем файл в память
        byte[] templateBytes = await File.ReadAllBytesAsync(templatePath);
        using var memoryStream = new MemoryStream();
        await memoryStream.WriteAsync(templateBytes);
        memoryStream.Position = 0;

        using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(memoryStream, true))
        {
            var body = wordDoc.MainDocumentPart?.Document.Body;
            if (body != null)
            {
                var dict = new Dictionary<string, string>
                {
                    { "{{FullName}}", request.FullName },
                    { "{{PassportSeriesNumber}}", request.PassportSeriesNumber },
                    { "{{PassportIssuedBy}}", request.PassportIssuedBy },
                    { "{{PassportIssueDate}}", request.PassportIssueDate },
                    { "{{DepartmentCode}}", request.DepartmentCode },
                    { "{{Address}}", request.Address },
                    { "{{Phone}}", request.Phone },
                    { "{{Email}}", request.Email },
                    
                    { "{{LotDescription}}", lot.Description ?? "" },
                    { "{{FedresursLink}}", lot.Bidding?.BankruptMessageId != Guid.Empty 
                        ? $"https://fedresurs.ru/bankruptmessages/{lot.Bidding!.BankruptMessageId}" 
                        : "Нет данных" },
                    { "{{MaxPrice}}", request.MaxPrice.HasValue ? request.MaxPrice.Value.ToString("N2") : "________________" }
                };

                foreach (var para in body.Elements<Paragraph>())
                {
                    var text = para.InnerText;
                    bool changed = false;

                    foreach (var kvp in dict)
                    {
                        if (text.Contains(kvp.Key))
                        {
                            text = text.Replace(kvp.Key, kvp.Value);
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        // Сохраняем форматирование (шрифт, размер, жирность) первого куска текста
                        var firstRun = para.Elements<Run>().FirstOrDefault();
                        var runProps = firstRun?.RunProperties?.CloneNode(true);

                        // Удаляем все куски текста, так как MS Word мог разбить плейсхолдер {{Метка}} на несколько Run
                        var runs = para.Elements<Run>().ToList();
                        foreach (var run in runs)
                        {
                            run.Remove();
                        }

                        // Создаем новый целый кусок текста с примененными заменами
                        var newRun = new Run(new Text(text));
                        if (runProps != null)
                        {
                            newRun.RunProperties = (RunProperties)runProps;
                        }
                        para.AppendChild(newRun);
                    }
                }
            }
            wordDoc.MainDocumentPart?.Document.Save();
        }

        return memoryStream.ToArray();
    }
}
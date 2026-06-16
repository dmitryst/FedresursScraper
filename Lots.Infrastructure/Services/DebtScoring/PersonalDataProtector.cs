using Lots.Data.Entities.DebtScoring;
using Microsoft.AspNetCore.DataProtection;

namespace FedresursScraper.Services.DebtScoring;

public interface IPersonalDataProtector
{
    bool IsPersonalData(ExtractedEntityType entityType);

    string Protect(string plaintext);

    string Unprotect(string protectedText);
}

public class PersonalDataProtector : IPersonalDataProtector
{
    private readonly IDataProtector _protector;

    public PersonalDataProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Lots.DebtScoring.PersonalData.v1");
    }

    public bool IsPersonalData(ExtractedEntityType entityType) =>
        entityType is ExtractedEntityType.Inn
            or ExtractedEntityType.Snils
            or ExtractedEntityType.BirthDate
            or ExtractedEntityType.RegistrationAddress
            or ExtractedEntityType.DebtorName;

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}

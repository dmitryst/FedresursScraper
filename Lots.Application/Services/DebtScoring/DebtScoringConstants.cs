namespace Lots.Application.Services.DebtScoring;

public static class DebtScoringConstants
{
    public const string DebtCategoryName = "Дебиторская задолженность";

    public static readonly string[] CourtDocumentExtensions = [".pdf", ".doc", ".docx"];

    public static readonly string[] CourtDocumentUrlPattern =
    [
        @"https?://[^\s<>""']+\.(?:pdf|doc|docx)(?:\?[^\s<>""']*)?",
    ];
}

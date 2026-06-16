namespace Lots.Data.Entities.DebtScoring;

public enum DebtLotProcessingStatus
{
    PendingDocuments = 0,
    ProcessingDocuments = 1,
    /// <summary>
    /// Метаданные извлечены (legacy; новые лоты переходят сразу в PendingEnrichment).
    /// </summary>
    DocumentsProcessed = 2,
    PendingEnrichment = 3,
    Rejected = 4,
    Failed = 5,
    ProcessingEnrichment = 6,
    EnrichmentCompleted = 7,
}

public enum CourtDocumentType
{
    Unknown = 0,
    CourtDecision = 1,
    CourtOrder = 2,
    WritOfExecution = 3,
    Other = 4,
}

public enum CourtDocumentProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
}

public enum ExtractedEntityType
{
    DebtorName = 0,
    Inn = 1,
    Snils = 2,
    Ogrn = 3,
    CaseNumber = 4,
    DebtBasis = 5,
    BirthDate = 6,
    RegistrationAddress = 7,
}

public enum EntityExtractionSource
{
    Regex = 0,
    Ocr = 1,
    /// <summary>
    /// Данные первичного парсинга торгов с Fedresurs (LegalCase, Subject).
    /// </summary>
    Fedresurs = 2,
}

public enum DebtEnrichmentStepStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
}

public enum DebtorCompanyStatus
{
    Unknown = 0,
    Active = 1,
    Liquidating = 2,
    Liquidated = 3,
    Bankrupt = 4,
}

public enum FsspProceedingStatus
{
    Unknown = 0,
    Open = 1,
    Closed = 2,
}

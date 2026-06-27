namespace FedresursScraper.Services;

public class AlignmentAttachmentPreviewDto
{
    public string Title { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string Extension { get; set; } = default!;
    public string? ExtractedText { get; set; }
    public string? DescriptionText { get; set; }
    public bool IsSummarized { get; set; }
    public string? SummarizationError { get; set; }
    public string? ExtractionError { get; set; }

    /// <summary>Прикрепить файл к лоту при применении.</summary>
    public bool SelectedForDownload { get; set; }

    /// <summary>Использовать извлечённый текст в описании.</summary>
    public bool UseForDescription { get; set; }
}

public class LotDescriptionAlignmentPreviewDto
{
    public int PublicId { get; set; }
    public Guid LotId { get; set; }
    public string? LotNumber { get; set; }
    public decimal? StartPrice { get; set; }
    public string? FedresursUrl { get; set; }
    public string? Error { get; set; }

    public string? CurrentDescription { get; set; }
    public string? CurrentViewingProcedure { get; set; }

    public string? TableDescription { get; set; }
    public bool IsReferralDescription { get; set; }

    public string? ProposedDescription { get; set; }
    public string? ProposedViewingProcedure { get; set; }

    public List<AlignmentAttachmentPreviewDto> Attachments { get; set; } = [];

    public bool CanApply =>
        !string.IsNullOrWhiteSpace(ProposedDescription) &&
        (!string.Equals(ProposedDescription.Trim(), CurrentDescription?.Trim(), StringComparison.Ordinal)
         || Attachments.Any(a => a.SelectedForDownload)
         || Attachments.Any(a => a.UseForDescription));
}

public class ApplyLotDescriptionAlignmentRequest
{
    public int PublicId { get; set; }
    public string Description { get; set; } = default!;
    public string? ViewingProcedure { get; set; }
    public List<ApplyAlignmentAttachmentRequest>? Attachments { get; set; }
}

public class ApplyAlignmentAttachmentRequest
{
    public string Title { get; set; } = default!;
    public string Extension { get; set; } = default!;
    public string? SourceUrl { get; set; }
}

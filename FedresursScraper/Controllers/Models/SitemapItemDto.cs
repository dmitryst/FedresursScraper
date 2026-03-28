public class SitemapItemDto
{
    public int PublicId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string? Slug { get; set; }
    public DateTime CreatedAt { get; set; }
}
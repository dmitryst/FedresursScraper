namespace FedresursScraper.Controllers.Models;

public class PaginatedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    public PaginatedResult(List<T> items, int count, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = count;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
    }
}

using System.ComponentModel.DataAnnotations;

public class CreateSessionRequest
{
    [Required]
    public string PlanId { get; set; } = string.Empty;
}
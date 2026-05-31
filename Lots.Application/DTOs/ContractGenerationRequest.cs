using System.ComponentModel.DataAnnotations;

namespace Lots.Application.DTOs;

public class ContractGenerationRequest
{
    [Required(ErrorMessage = "ФИО обязательно")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Серия и номер паспорта обязательны")]
    public string PassportSeriesNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Кем выдан паспорт обязательно")]
    public string PassportIssuedBy { get; set; } = string.Empty;

    [Required(ErrorMessage = "Дата выдачи паспорта обязательна")]
    public string PassportIssueDate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Код подразделения обязателен")]
    public string DepartmentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Адрес регистрации обязателен")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Телефон обязателен")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email обязателен")]
    [EmailAddress(ErrorMessage = "Некорректный формат Email")]
    public string Email { get; set; } = string.Empty;

    public decimal? MaxPrice { get; set; }
}
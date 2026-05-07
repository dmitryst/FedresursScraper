using System.ComponentModel.DataAnnotations;

namespace FedresursScraper.UserAds;

public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxFileSize;
        public MaxFileSizeAttribute(int maxFileSize) => _maxFileSize = maxFileSize;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is List<IFormFile> files)
            {
                foreach (var file in files)
                {
                    if (file.Length > _maxFileSize)
                        return new ValidationResult($"Файл {file.FileName} превышает лимит {_maxFileSize / 1024 / 1024} МБ.");
                }
            }
            return ValidationResult.Success;
        }
    }
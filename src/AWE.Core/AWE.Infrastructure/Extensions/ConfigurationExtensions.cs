using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace AWE.Infrastructure.Extensions;

public static class ConfigurationExtensions
{
    public static T GetOptions<T>(this IConfiguration configuration, string sectionName)
        where T : class, new()
    {
        var options = new T();
        configuration.GetSection(sectionName).Bind(options);

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), validationResults, true))
        {
            var errors = string.Join(", ", validationResults.Select(r => r.ErrorMessage));
           
            throw new InvalidOperationException($"MISSING CONFIG '{sectionName}': {errors}");
        }

        return options;
    }
}

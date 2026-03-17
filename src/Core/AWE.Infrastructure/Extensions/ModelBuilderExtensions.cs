using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AWE.Infrastructure.Extensions;
/// <summary>
/// Extension for setup model builder
/// Change convention name table, field when create in database
/// </summary>
public static class ModelBuilderExtensions
{
    public static void ApplySnakeCaseColumnNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // Lấy tên property gốc (ví dụ: CreatedAt)
                var storeObjectIdentifier = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
                var columnName = property.GetColumnName(storeObjectIdentifier.GetValueOrDefault());

                // Chuyển thành snake_case (ví dụ: created_at)
                property.SetColumnName(ToSnakeCase(columnName));
            }

            // 3. (Tuỳ chọn) Chuyển cả tên Key (PK, FK) và Index sang snake_case cho đồng bộ
            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()));
            }

            foreach (var key in entity.GetForeignKeys())
            {
                key.SetConstraintName(ToSnakeCase(key.GetConstraintName()));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()));
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var startUnderscores = Regex.Match(input, @"^_+");
        return startUnderscores + Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLower();
    }
}

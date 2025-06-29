using BarkFluff.Users.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Contexts;

/// <summary>
/// Расширения для контекста базы данных UsersContext
/// </summary>
public static class UsersContextExtensions
{
    /// <summary>
    /// Настраивает функции полнотекстового поиска для UsersContext
    /// </summary>
    public static void ConfigureFullTextSearch(this ModelBuilder modelBuilder)
    {
        // Регистрация расширения pg_trgm для поддержки триграмм
        modelBuilder.HasPostgresExtension("pg_trgm");
        
        // Регистрация расширения для полнотекстового поиска
        modelBuilder.HasPostgresExtension("btree_gin");
        
        // Поддержка функций полнотекстового поиска встроена в Npgsql.EntityFrameworkCore.PostgreSQL
        // через EF.Functions, поэтому нам не нужно регистрировать их вручную
    }
}

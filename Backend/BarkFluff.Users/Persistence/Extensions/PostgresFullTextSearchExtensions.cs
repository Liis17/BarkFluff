namespace BarkFluff.Users.Persistence.Extensions;

/// <summary>
/// Расширения для работы с полнотекстовым поиском в PostgreSQL через Entity Framework Core
/// </summary>
public static class PostgresFullTextSearchExtensions
{
    /// <summary>
    /// Расширения для поиска с использованием триграмм
    /// </summary>
    public static bool TrigramMatches(this string text, string pattern, double threshold = 0.3)
    {
        // Этот метод предназначен для использования в LINQ запросах EF Core
        throw new NotSupportedException("Этот метод предназначен только для перевода в SQL и не должен вызываться напрямую");
    }

    /// <summary>
    /// Расширение для выполнения полнотекстового поиска в EF Core с использованием Npgsql
    /// </summary>
    public static IQueryable<T> FullTextSearch<T>(this IQueryable<T> source,
        string searchTerm,
        params string[] propertyNames) where T : class
    {
        // Это просто вспомогательный метод, мы будем использовать
        // стандартные функции EF Core для полнотекстового поиска
        throw new NotImplementedException();
    }
}

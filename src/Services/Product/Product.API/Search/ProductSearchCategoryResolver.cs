using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.Search;

internal interface IProductSearchCategoryResolver
{
    Task<string?> ResolveCategoryNameAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ProductSearchCategoryResolver(ProductDbContext db) : IProductSearchCategoryResolver
{
    public async Task<string?> ResolveCategoryNameAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.Description })
            .ToListAsync(cancellationToken);

        if (request.CategoryId is > 0)
        {
            return categories.FirstOrDefault(c => c.Id == request.CategoryId.Value)?.Name;
        }

        if (string.IsNullOrWhiteSpace(request.Keyword))
            return null;

        var normalizedKeyword = EndpointHelpers.NormalizeVietnamese(request.Keyword);
        char[] separators = [' ', ',', '.', ';', '?', '!', '-', '_'];
        var keywordWords = normalizedKeyword.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var bestCategoryMatch = categories
            .Select(category =>
            {
                var normalizedName = EndpointHelpers.NormalizeVietnamese(category.Name ?? string.Empty);
                var normalizedDescription = EndpointHelpers.NormalizeVietnamese(category.Description ?? string.Empty);

                if (normalizedName == normalizedKeyword)
                    return new { Category = category, Score = 100 };

                var nameWords = normalizedName
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var descriptionWords = normalizedDescription
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var matchedWordsInName = keywordWords.Count(nameWords.Contains);
                var matchedWordsInDescription = keywordWords.Count(descriptionWords.Contains);

                return new { Category = category, Score = Math.Max(matchedWordsInName, matchedWordsInDescription) };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        return bestCategoryMatch?.Category.Name;
    }
}

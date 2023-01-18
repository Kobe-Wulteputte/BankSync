namespace BS.Data;

public class CategoryGuess
{
    public Dictionary<CategoryEnum, double> CategoryProbabilities { get; }

    public (CategoryEnum, double) MostLikelyCategory
    {
        get
        {
            var mostLikely = CategoryProbabilities.MaxBy(x => x.Value);
            return (mostLikely.Key, mostLikely.Value);
        }
    }

    public CategoryGuess()
    {
        CategoryProbabilities = new Dictionary<CategoryEnum, double>();
        foreach (var value in Enum.GetValues(typeof(CategoryEnum)))
        {
            CategoryProbabilities[(CategoryEnum)value] = 0;
        }
    }

    public void Combine(CategoryGuess guess)
    {
        foreach (var category in CategoryProbabilities.Keys)
        {
            CategoryProbabilities[category] += guess.CategoryProbabilities[category];
        }
    }
}
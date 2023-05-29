using BS.Data;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace BS.Logic.CategoryGuesser;

public class AiCategoryGuesserService
{
    private readonly IOpenAIService _openAiService;
    private readonly ILogger<AiCategoryGuesserService> _logger;

    public AiCategoryGuesserService(IOpenAIService openAiService, ILogger<AiCategoryGuesserService> logger)
    {
        _openAiService = openAiService;
        _logger = logger;
    }

    private async Task<string> GetModel()
    {
        // var models = await _openAiService.FineTunes.ListFineTunes();
        return "curie:ft-personal-2023-05-29-14-34-57";
    }

    public async Task<CategoryEnum?> Guess(Expense expense)
    {
        _logger.LogInformation($"Guessing category for {expense.Name}");
        var completionResult = await _openAiService.Completions.CreateCompletion(new CompletionCreateRequest
        {
            MaxTokens = 10,
            Prompt = expense.AiPrompt,
            Model = await GetModel(),
            Stop = " END",
            Temperature = 0,
            LogProbs = 1
        });
        if (!completionResult.Successful)
        {
            _logger.LogError($"Aicompletion not successful. Error: {completionResult.Error}");
            return null;
        }

        var logprob = completionResult.Choices.FirstOrDefault().LogProbs.TokenLogProbs.FirstOrDefault();
        var percentage = Math.Round(Math.Pow(Math.E, logprob) * 100, 2);
        var category = completionResult.Choices.FirstOrDefault().Text.Replace(" ", "").Replace("_", " ");
        _logger.LogInformation($"Category: {category}");
        _logger.LogInformation($"Percentage: {percentage}%");

        if (percentage < 60)
        {
            _logger.LogInformation($"Percentage too low, not guessing category for {expense.Name}");
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<CategoryEnum?>($"\"{category}\"");
        }
        catch (Exception e)
        {
            return null;
        }
    }
}
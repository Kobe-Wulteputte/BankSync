using Betalgo.Ranul.OpenAI.Contracts.Enums;
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using BS.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BS.Logic.CategoryGuesser;

public class AiCategoryGuesserService(IOpenAIService openAiService, ILogger<AiCategoryGuesserService> logger, IConfiguration config)
{
    private string GetModel()
    {
        return config["OpenAIServiceOptions:Model"] ?? throw new InvalidOperationException("OpenAIServiceOptions:Model is not configured");
    }

    public async Task<CategoryEnum?> Guess(Expense expense)
    {
        logger.LogInformation($"Guessing category for {expense.Name}");
        var completionResult = await openAiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
        {
            MaxTokens = 10,
            Messages = new List<ChatMessage>
            {
                new()
                {
                    Role = ChatCompletionRole.System,
                    Content =
                        $"You categorize bank transactions. Reply with exactly one of these categories: {string.Join(", ", Enum.GetNames<CategoryEnum>())}."
                },
                new()
                {
                    Role = ChatCompletionRole.User,
                    Content = expense.AiPrompt
                }
            },
            Model = GetModel(),
            Stop = " END",
            Temperature = 0,
            LogProbs = true
        });
        if (!completionResult.Successful)
        {
            logger.LogError($"Aicompletion not successful. Error: {completionResult.Error}");
            return null;
        }

        var logprob = completionResult.Choices.First().LogProbs.Content.Sum(resp => resp.LogProb);
        var percentage = Math.Round(Math.Pow(Math.E, logprob) * 100, 2);
        var category = completionResult.Choices.First().Message.Content.Replace(" ", "").Replace("_", " ").Replace("[", " ").Replace("]", " ");
        logger.LogInformation($"Category: {category}");
        logger.LogInformation($"Percentage: {percentage}%");

        if (percentage < 60)
        {
            logger.LogInformation($"Percentage too low, not guessing category for {expense.Name}");
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
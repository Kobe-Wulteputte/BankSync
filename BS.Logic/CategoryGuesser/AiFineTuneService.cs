using OpenAI.Interfaces;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels.FineTuneResponseModels;

namespace BS.Logic.CategoryGuesser;

public static class AiFineTuneService
{
    public static async Task Train(IOpenAIService sdk)
    {
        try
        {
            const string fileName = "C:/Users/kwlt/Downloads/data_prepared.jsonl";
            var sampleFile = await File.ReadAllBytesAsync(fileName);

            Console.WriteLine($"Uploading file {fileName}");
            var uploadFilesResponse = await sdk.Files.FileUpload(UploadFilePurposes.UploadFilePurpose.FineTune, sampleFile, fileName);
            if (uploadFilesResponse.Successful)
            {
                Console.WriteLine($"{fileName} uploaded");
            }
            else
            {
                Console.WriteLine($"{fileName} failed");
            }

            var createFineTuneResponse = await sdk.FineTunes.CreateFineTune(new FineTuneCreateRequest
            {
                TrainingFile = uploadFilesResponse.Id,
                Model = Models.Curie,
            });

            var listFineTuneEventsStream = await sdk.FineTunes.ListFineTuneEvents(createFineTuneResponse.Id, true);
            using var streamReader = new StreamReader(listFineTuneEventsStream);
            while (!streamReader.EndOfStream)
            {
                Console.WriteLine(await streamReader.ReadLineAsync());
            }

            do
            {
                FineTuneResponse retrieveFineTuneResponse = await sdk.FineTunes.RetrieveFineTune(createFineTuneResponse.Id);
                if (retrieveFineTuneResponse.Status is "succeeded" or "cancelled" or "failed")
                {
                    Console.WriteLine($"Fine-tune Status for {createFineTuneResponse.Id}: {retrieveFineTuneResponse.Status}.");
                    break;
                }

                Console.WriteLine(
                    $"Fine-tune Status for {createFineTuneResponse.Id}: {retrieveFineTuneResponse.Status}. Wait 10 more seconds");
                await Task.Delay(10_000);
            } while (true);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public static async Task Test(IOpenAIService sdk)
    {
        var completionResult = await sdk.Completions.CreateCompletion(new CompletionCreateRequest
        {
            MaxTokens = 5,
            Prompt =
                @"Type: ARGENTA_ARSPBE22, Amount: -50,00, Date: 13-04-2023, Name: Mare Wulteputte , Description:  ->",
            Model = "curie:ft-personal-2023-05-29-14-34-57",
            Temperature = 0,
            Stop = " END",
            LogProbs = 1
        });
        if (completionResult.Successful)
        {
            Console.WriteLine(completionResult.Choices.FirstOrDefault().Text);
            var logprob = completionResult.Choices.FirstOrDefault().LogProbs.TokenLogProbs.FirstOrDefault();
            var percentage1 = Math.Round(Math.Pow(Math.E, logprob) * 100, 2);
            Console.WriteLine($"Percentage: {percentage1}%");
        }
    }

    public static async Task CleanUpAllFineTunings(IOpenAIService sdk)
    {
        var fineTunes = await sdk.FineTunes.ListFineTunes();
        foreach (var datum in fineTunes.Data)
        {
            await sdk.FineTunes.DeleteFineTune(datum.FineTunedModel);
        }
    }
}
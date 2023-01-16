using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BS.Logic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace BS.Function;

public class ExpenseCategoryLearner
{
    private readonly ILogger<ExpenseCategoryLearner> _logger;
    private readonly InstitutionService _institutionService;

    public ExpenseCategoryLearner(ILogger<ExpenseCategoryLearner> logger,
        InstitutionService institutionService)
    {
        _logger = logger;
        _institutionService = institutionService;
    }

    [Function("LearnExpenses")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req)
    {
        var sw = new Stopwatch();
        sw.Restart();

        var institution = await _institutionService.GetUsedInstitutions();
        _logger.LogInformation("institution: {institution}", institution);

        var response = req.CreateResponse(HttpStatusCode.OK);

        response.Headers.Add("Date", "Mon, 18 Jul 2016 16:06:00 GMT");
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        response.WriteString(JsonSerializer.Serialize(institution));

        // _logger.LogMetric(@"funcExecutionTimeMs", sw.Elapsed.TotalMilliseconds,
        //     new Dictionary<string, object>
        //     {
        //         { "foo", "bar" },
        //         { "baz", 42 }
        //     }
        // );

        return response;
    }
}
using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Agreements;

namespace BS.Logic.Nordigen;

public class EndUserAgreementService
{
    private readonly INordigenClient _nordigenClient;

    public EndUserAgreementService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }


    public async Task<EndUserAgreement> CreateEndUserAgreement(string institutionId)
    {
        // End user agreement has option for max historical days and max valid days, default both 90 days
        EndUserAgreement endUserAgreement =
            await _nordigenClient.Agreements.Post(new EndUserAgreementCreation(institutionId, 90, 90,
                new() { "balances", "details", "transactions" }));
        return endUserAgreement;
    }

    public async Task DeleteAllEndUserAgreements()
    {
        var agreements = _nordigenClient.Agreements.Get();
        await foreach (var agreement in agreements)
        {
            try
            {
                await _nordigenClient.Agreements.Delete(agreement.Id);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
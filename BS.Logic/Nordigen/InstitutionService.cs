using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Institutions;

namespace BS.Logic.Nordigen;

public class InstitutionService
{
    public static readonly string RevolutInstitutionId = "REVOLUT_REVOGB21";
    public static readonly string ArgentaInstitutionId = "ARGENTA_ARSPBE22";
    public static readonly string PayPalInstitutionId = "PAYPAL_PPLXLULL";
    private readonly INordigenClient _nordigenClient;

    public InstitutionService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<List<Institution>> GetAll(string countryCode = "BE")
    {
        var institutions = await _nordigenClient.Institutions.GetByCountry(countryCode);
        return institutions;
    }

    public async Task<List<Institution>> GetUsedInstitutions()
    {
        return new List<Institution>
        {
            await _nordigenClient.Institutions.Get(RevolutInstitutionId),
            await _nordigenClient.Institutions.Get(ArgentaInstitutionId),
            await _nordigenClient.Institutions.Get(PayPalInstitutionId)
        };
    }
}
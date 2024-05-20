using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic.Nordigen;

public class RequisitionService
{
    private readonly INordigenClient _nordigenClient;

    public RequisitionService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<Requisition> New(string institutionId, string? userName, Guid? agreement = null)
    {
        var creation = new RequisitionCreation(new Uri("http://localhost"), institutionId);
        creation.Reference = userName;
        creation.Agreement = agreement;
        return await _nordigenClient.Requisitions.Post(creation);
    }

    public IAsyncEnumerable<Requisition> GetAll()
    {
        return _nordigenClient.Requisitions.Get();
    }

    public async Task<Requisition> Get(Guid id)
    {
        return await _nordigenClient.Requisitions.Get(id);
    }

    public async Task Delete(Guid id)
    {
        try
        {
            await _nordigenClient.Requisitions.Delete(id);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task DeleteAllRequisitions()
    {
        var reqs = _nordigenClient.Requisitions.Get();
        await foreach (var req in reqs)
        {
            try
            {
                await _nordigenClient.Requisitions.Delete(req.Id);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
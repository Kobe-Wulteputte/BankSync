﻿using VMelnalksnis.NordigenDotNet;
using VMelnalksnis.NordigenDotNet.Agreements;

namespace BS.Logic;

public class EndUserAgreementService
{
    private readonly INordigenClient _nordigenClient;
    private Guid _euaId = new Guid("12323b4f-6540-4fce-8f84-0cce46d36f25");

    public Guid EuaId => _euaId;

    public EndUserAgreementService(INordigenClient nordigenClient)
    {
        _nordigenClient = nordigenClient;
    }

    public async Task<EndUserAgreement> CreateEndUserAgreement(string institutionId)
    {
        // End user agreement has option for max historical days and max valid days, default both 90 days
        var endUserAgreement = await _nordigenClient.Agreements.Post(new EndUserAgreementCreation(institutionId));
        return endUserAgreement;
    }
}
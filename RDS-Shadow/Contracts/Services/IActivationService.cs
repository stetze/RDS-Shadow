namespace RDS_Shadow.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}

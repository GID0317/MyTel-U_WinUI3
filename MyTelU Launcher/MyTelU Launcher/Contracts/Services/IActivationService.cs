namespace MyTelU_Launcher.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}

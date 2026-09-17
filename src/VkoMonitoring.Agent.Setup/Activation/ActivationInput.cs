namespace VkoMonitoring.Agent.Setup.Activation;

public sealed record ActivationInput(
    Uri ServerAddress,
    string ActivationCode,
    string DeviceName,
    string? Room,
    string ConnectionType);

namespace mGO.Log.Exceptions;

/// <summary>Invalid mGO.Log configuration.</summary>
public sealed class GOLogConfigurationException(string message) : ArgumentException(message);

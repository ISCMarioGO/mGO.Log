namespace GO.Log.Exceptions;

/// <summary>Invalid GO.Log configuration.</summary>
public sealed class GOLogConfigurationException(string message) : ArgumentException(message);

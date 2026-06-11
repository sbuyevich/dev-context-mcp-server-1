namespace DevContextMcp.Server.Core.Services;

public sealed class IndexUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

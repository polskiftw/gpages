using GDupe.Core.Abstractions;

namespace GDupe.Core.Services;

public sealed class NullLogger : IAppLogger
{
    public void Info(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

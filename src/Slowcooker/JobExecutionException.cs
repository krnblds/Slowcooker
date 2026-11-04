namespace Slowcooker;

/// <summary>Thrown when a job cannot be executed because its type, method or arguments cannot be resolved.</summary>
public sealed class JobExecutionException : Exception
{
    public JobExecutionException(string message) : base(message) { }
    public JobExecutionException(string message, Exception inner) : base(message, inner) { }
}

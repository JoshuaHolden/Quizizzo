namespace ClickBaitThumbnailGenerator;

public sealed class OpenAiRequestException(
    string message,
    int? statusCode = null,
    TimeSpan? retryAfter = null,
    bool transient = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool IsTransient { get; } = transient;
}

public interface IRetryPolicy
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

public sealed class RetryPolicy(
    int maximumRetries,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<double>? jitter = null) : IRetryPolicy
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Func<double> _jitter = jitter ?? Random.Shared.NextDouble;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < maximumRetries && IsTransient(exception))
            {
                var serverDelay = (exception as OpenAiRequestException)?.RetryAfter;
                var exponential = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
                var wait = serverDelay ?? exponential + TimeSpan.FromMilliseconds(_jitter() * 500);
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        OpenAiRequestException api => api.IsTransient,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false
    };
}

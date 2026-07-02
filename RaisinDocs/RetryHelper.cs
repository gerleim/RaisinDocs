namespace RaisinDocs;

internal static class RetryHelper
{
    internal static T? Execute<T>(Func<T> action, int maxRetries = 3, int delayMs = 100,
        Action<Exception, int>? onRetry = null) where T : class
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                onRetry?.Invoke(ex, i + 1);
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                onRetry?.Invoke(ex, i + 1);
            }
        }
        return null;
    }

    internal static bool Execute(Action action, int maxRetries = 3, int delayMs = 100,
        Action<Exception, int>? onRetry = null)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                onRetry?.Invoke(ex, i + 1);
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                onRetry?.Invoke(ex, i + 1);
            }
        }
        return false;
    }
}

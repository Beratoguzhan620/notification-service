namespace NotificationService.IntegrationTests.Support;

public static class Waiting
{
    /// <summary>Polls a notification until it reaches <paramref name="status"/> (or fails the test after the timeout).</summary>
    public static async Task<NotificationDto> WaitForStatusAsync(this HttpClient client, Guid id, string status, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        NotificationDto current;
        do
        {
            current = await client.GetNotificationAsync(id);
            if (current.Status == status)
            {
                return current;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Notification {id} is {current.Status}, not {status}. History: " +
            string.Join(" → ", current.History!.Select(h => h.Status)));
    }

    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        T value;
        do
        {
            value = await read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return value;
    }
}

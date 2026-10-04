using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkflowCore.Dashboard.Journal;

/// <summary>
/// Buffers activity entries and writes them to the journal in batches, off the workflow execution path.
/// If the journal store is unavailable, the oldest buffered entries are dropped rather than growing memory.
/// </summary>
public sealed class JournalWriter : BackgroundService
{
    private const int BufferSize = 50_000;
    private const int BatchSize = 500;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    private readonly IDashboardJournal _journal;
    private readonly ILogger<JournalWriter> _logger;
    private readonly Channel<ActivityEntry> _channel = Channel.CreateBounded<ActivityEntry>(
        new BoundedChannelOptions(BufferSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public JournalWriter(IDashboardJournal journal, ILogger<JournalWriter> logger)
    {
        _journal = journal;
        _logger = logger;
    }

    public void Enqueue(ActivityEntry entry) => _channel.Writer.TryWrite(entry);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _channel.Reader;
        var batch = new List<ActivityEntry>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                // Collect for a short while so bursts of step events become one write.
                using (var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    window.CancelAfter(FlushInterval);
                    try
                    {
                        while (batch.Count < BatchSize && await reader.WaitToReadAsync(window.Token))
                        {
                            while (batch.Count < BatchSize && reader.TryRead(out var entry))
                                batch.Add(entry);
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Flush interval elapsed.
                    }
                }

                await Flush(batch, stoppingToken);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Write whatever is left on shutdown.
        while (reader.TryRead(out var entry))
            batch.Add(entry);
        if (batch.Count > 0)
            await Flush(batch, CancellationToken.None);
    }

    private async Task Flush(List<ActivityEntry> batch, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _journal.AppendAsync(batch, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < RetryDelays.Length)
            {
                _logger.LogWarning(ex, "Writing {Count} activity entries to the dashboard journal failed; retrying", batch.Count);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Dropped {Count} activity entries after repeated journal write failures", batch.Count);
                return;
            }
        }
    }
}

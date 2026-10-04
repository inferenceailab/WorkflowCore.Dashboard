using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.Designer;

public static class DesignerExtensions
{
    /// <summary>
    /// Adds the visual designer for JSON/YAML (DSL) workflows to the dashboard. Published definitions are stored in
    /// the dashboard journal, so configure a persistent journal (e.g. <c>UseEntityFrameworkJournal</c>) to keep them
    /// across restarts. Requires <c>AllowActions</c>.
    /// </summary>
    public static DashboardBuilder AddDesigner(this DashboardBuilder builder, Action<DesignerOptions>? configure = null)
    {
        var services = builder.Services;
        var options = services.AddOptions<DesignerOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddWorkflowDSL();
        services.TryAddSingleton<StepCatalog>();
        services.TryAddSingleton<DesignerStore>();
        services.TryAddSingleton<DefinitionValidator>();
        services.TryAddSingleton<DesignerService>();
        services.AddSingleton<IDashboardExtension, DesignerApi>();
        services.AddHostedService<DesignerSync>();
        return builder;
    }
}

/// <summary>
/// Registers published designer workflows with Workflow Core: all of them at startup (before later hosted
/// services, such as one that starts the workflow host), then new versions from other nodes periodically.
/// </summary>
internal sealed class DesignerSync : IHostedService, IDisposable
{
    private readonly DesignerService _designer;
    private readonly DesignerOptions _options;
    private readonly ILogger<DesignerSync> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public DesignerSync(DesignerService designer, IOptions<DesignerOptions> options, ILogger<DesignerSync> logger)
    {
        _designer = designer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await SyncOnce(cancellationToken);
        _loop = Loop(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_loop is not null)
            await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task Loop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.SyncInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SyncOnce(ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncOnce(CancellationToken ct)
    {
        try
        {
            var added = await _designer.RegisterStoredVersions(ct);
            if (added > 0)
                _logger.LogInformation("Registered {Count} designer workflow version(s)", added);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Usually the journal database being unavailable; the next sync retries.
            _logger.LogWarning(ex, "Could not load designer workflows from the journal");
        }
    }

    public void Dispose() => _stopping.Dispose();
}

using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrderFlow.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Registers MassTransit over RabbitMQ plus health checks. Tests replace the
    /// transport with the in-memory test harness (AddMassTransitTestHarness).
    /// </summary>
    public static IHostApplicationBuilder AddOrderFlowServiceDefaults(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<IBusRegistrationConfigurator>? configureBus = null)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(TimeProvider.System);

        // Take requests only once connected to the broker: a copy that accepts a browser's live
        // connection before its own live-updates queue exists would miss pushes (spec 013).
        builder.Services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true);

        builder.Services.AddMassTransit(bus =>
        {
            // Queues are prefixed per service ("notification-order-placed") so they never
            // collide with event exchanges ("order-placed") or with another service's queues.
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(prefix: serviceName, includeNamespace: false));
            configureBus?.Invoke(bus);

            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbit = builder.Configuration.GetSection("RabbitMq");
                var port = ushort.TryParse(rabbit["Port"], out var configuredPort) ? configuredPort : (ushort)5672;
                cfg.Host(rabbit["Host"] ?? "localhost", port, rabbit["VirtualHost"] ?? "/", host =>
                {
                    host.Username(rabbit["Username"] ?? "guest");
                    host.Password(rabbit["Password"] ?? "guest");
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    public static WebApplication MapOrderFlowDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.MapHealthChecks("/health");
        return app;
    }
}

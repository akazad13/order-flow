using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using MassTransit;
using System.Net;
using System.Net.Sockets;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace OrderFlow.IntegrationTesting;

/// <summary>
/// One PostgreSQL and one RabbitMQ container per test run. Tests isolate themselves with
/// their own database and their own RabbitMQ virtual host, so they never see each other's
/// rows or messages.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    private const string RabbitUser = "orderflow";
    private const string RabbitPassword = "orderflow";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public InfrastructureFixture()
    {
        // A fixed host port survives a broker stop/start (spec 007, AC-3); a random one would change.
        RabbitPort = FreeTcpPort();
        Rabbit = new RabbitMqBuilder("rabbitmq:4.1-alpine")
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .WithPortBinding(RabbitPort, RabbitMqBuilder.RabbitMqPort)
            .Build();
    }

    public RabbitMqContainer Rabbit { get; }

    public ushort RabbitPort { get; }

    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), Rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await Rabbit.DisposeAsync();
    }

    /// <summary>A connection string to a database of the test's own (EF migrations create it).</summary>
    public string ConnectionString(string database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = _postgres.GetConnectionString() };
        builder["Database"] = database;
        return builder.ConnectionString;
    }

    public async Task<string> CreateVirtualHostAsync()
    {
        var vhost = $"test-{Guid.NewGuid():N}"[..13];
        await RabbitCtl("add_vhost", vhost);
        await RabbitCtl("set_permissions", "-p", vhost, RabbitUser, ".*", ".*", ".*");
        return vhost;
    }

    /// <summary>The settings a service needs to use this broker (see ServiceDefaults).</summary>
    public Dictionary<string, string?> RabbitSettings(string vhost) => new()
    {
        ["RabbitMq:Host"] = "localhost",
        ["RabbitMq:Port"] = RabbitPort.ToString(CultureInfo.InvariantCulture),
        ["RabbitMq:VirtualHost"] = vhost,
        ["RabbitMq:Username"] = RabbitUser,
        ["RabbitMq:Password"] = RabbitPassword,
    };

    internal (string User, string Password) RabbitCredentials => (RabbitUser, RabbitPassword);

    /// <summary>A bare bus on the broker, for setting up topology the services under test would not (spec 018).</summary>
    public IBusControl CreateBus(string vhost, Action<IRabbitMqBusFactoryConfigurator> configure) =>
        Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host("localhost", RabbitPort, vhost, h =>
            {
                h.Username(RabbitUser);
                h.Password(RabbitPassword);
            });
            configure(cfg);
        });

    /// <summary>Messages in a queue right now (rabbitmqctl's live count); null if there is no such queue.</summary>
    public async Task<int?> QueueDepthAsync(string vhost, string queue)
    {
        var result = await Rabbit.ExecAsync(["rabbitmqctl", "list_queues", "-p", vhost, "name", "messages", "--formatter", "json"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl list_queues failed: {result.Stderr}");
        }

        foreach (var row in JsonDocument.Parse(result.Stdout).RootElement.EnumerateArray())
        {
            if (row.GetProperty("name").GetString() == queue)
            {
                return row.GetProperty("messages").GetInt32();
            }
        }

        return null;
    }

    private async Task RabbitCtl(params string[] args)
    {
        var result = await Rabbit.ExecAsync(["rabbitmqctl", .. args]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl {string.Join(' ', args)} failed: {result.Stderr}");
        }
    }

    private static ushort FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>
/// Name of the xUnit collection all integration tests share (one fixture, run sequentially:
/// one test stops the broker, spec 007 AC-3). xUnit v2 needs the [CollectionDefinition] in
/// each test assembly; see IntegrationCollectionDefinition there.
/// </summary>
public static class IntegrationCollection
{
    public const string Name = "integration";
}

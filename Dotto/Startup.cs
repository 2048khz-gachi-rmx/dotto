using Dotto.Application;
using Dotto.Commands;
using Dotto.Common.DateTimeProvider;
using Dotto.HostedServices;
using Dotto.Infrastructure.Database;
using Dotto.Infrastructure.Downloader;
using Dotto.Infrastructure.FileUpload;
using Dotto.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.Commands;

var builder = Host.CreateApplicationBuilder(args);

var settings = builder.Configuration.GetSection("Dotto").Get<DottoSettings>();
if (settings == null)
   throw new ArgumentNullException(nameof(DottoSettings));

builder.Services.AddSingleton(settings);

#region Discord

builder.Services
    .AddDiscordGateway((opt) =>
    {
        opt.Intents = GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
                | GatewayIntents.GuildMessageTyping;
    })
    .AddCommands()
    .AddApplicationCommands()
    .AddGatewayEventHandlers(typeof(Program).Assembly);

#endregion

#region Dotto

// Infrastructure
builder.Services
    .AddDatabase(settings?.ConnectionString)
    .AddFileUploader(settings?.Minio)
    .AddDownloader();

// Application
builder.Services
    .AddSingleton<IDateTimeProvider, DateTimeProvider>()
    .AddApplication()
    .AddCommandServices();

// Hosted services
builder.Services
    .AddHostedService<ChannelFlagPoller>();

#endregion

var host = builder.Build();

host.AddModules(typeof(CommandAssemblyMarker).Assembly);
host.UseGatewayEventHandlers();
_ = host.InitializeMinioUploader(); // don't care about waiting for bucket creation lololo

await host.MigrateDatabase();
await host.RunAsync();
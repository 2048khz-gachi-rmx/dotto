using Dotto.Application;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord;
using Dotto.Discord.Commands;
using Dotto.Discord.ResultHandlers;
using Dotto.HostedServices;
using Dotto.Infrastructure.Database;
using Dotto.Infrastructure.Downloader;
using Dotto.Infrastructure.FileUpload;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.Commands;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;

var builder = Host.CreateApplicationBuilder(args);

#region Netcord

builder.Services
    .AddDiscordGateway((opt) =>
    {
        opt.Intents = GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
                | GatewayIntents.GuildMessageTyping;
    })
    .AddCommands(cfg =>
    {
        cfg.ResultHandler = new DottoCommandResultHandler<CommandContext>();
    })
    .AddApplicationCommands(cfg =>
    {
        cfg.ResultHandler = new DottoApplicationCommandServiceResultHandler<ApplicationCommandContext>();
    })
    .AddGatewayEventHandlers(typeof(Program).Assembly);

#endregion

#region Dotto

// Infrastructure
builder.Services
    .AddDatabase(builder.Configuration.GetRequiredSection("ConnectionString").Value)
    .AddFileUploader(builder.Configuration.GetRequiredSection("Minio"))
    .AddDownloader(builder.Configuration.GetRequiredSection("Downloader"));

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
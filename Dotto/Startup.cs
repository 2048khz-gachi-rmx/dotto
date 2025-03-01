using Dotto.Commands;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.Commands;
using Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplicationServices()
    .AddDiscordGateway((opt) =>
    {
        opt.Intents = GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
                | GatewayIntents.GuildMessageTyping;
    })
    .AddCommands()
    .AddCommandServices()
    .AddApplicationCommands()
    .AddGatewayEventHandlers(typeof(Program).Assembly);
    
var host = builder.Build();

host.AddModules(typeof(CommandAssemblyMarker).Assembly);
host.UseGatewayEventHandlers();

await host.RunAsync();
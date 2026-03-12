/// <summary>
/// Plugin module: registers preset and voting services with the AssettoServer container.
///
/// Purpose:
/// Load() is called by AssettoServer at startup. It registers: (1) PresetConfigurationManager
/// (single instance) – loads presets from the presets folder and plugin config, exposes
/// VotingPresetTypes and AllPresetTypes; (2) PresetManager (single instance) – applies preset
/// changes (restart path, reconnect/kick); (3) VotingPresetPlugin as IHostedService (single
/// instance) – runs the timer vote loop and handles commands. ReferenceConfiguration
/// supplies default YAML values so the server can generate or validate the config file.
/// </summary>
using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;
using VotingPresetPlugin.Preset;

namespace VotingPresetPlugin;

public class VotingPresetModule : AssettoServerModule<VotingPresetConfiguration>
{
    /// <summary>
    /// Register PresetConfigurationManager (loads presets), PresetManager (applies preset), and VotingPresetPlugin (timer vote loop + command handlers) as single instances. Command module is registered by AssettoServer via discovery (VotingPresetCommandModule).
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<PresetConfigurationManager>().AsSelf().SingleInstance();
        builder.RegisterType<PresetManager>().AsSelf().SingleInstance();
        builder.RegisterType<VotingPresetPlugin>().AsSelf().As<IHostedService>().SingleInstance();
    }

    /// <summary>Default config values used when generating or validating the plugin config file (e.g. EnableReconnect, IntervalMinutes, TransitionDelaySeconds).</summary>
    public override VotingPresetConfiguration ReferenceConfiguration => new()
    {
        EnableReconnect = true,
        EnableVote = false,
        EnableStayOnTrack = false,
        IntervalMinutes = 60,
        TransitionDelaySeconds = 30,
        TransitionDurationSeconds = 10,
        Meta = new()
        {
            Name = "SRP",
            AdminOnly = false
        }
    };
}

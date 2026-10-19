using FikaServer.Models.Fika.Routes.Client;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.RaidSettings;

namespace PrivateStack.FairPlay.Server;

public static class RaidRules
{
    public const string Denial = "FAIRPLAY_HEADLESS_REQUIRED";
    public static void Host(bool configuredHeadless, bool connectedHeadless, bool sameIdentity, bool spectator)
    {
        if (!configuredHeadless || !connectedHeadless || !sameIdentity || spectator) throw new InvalidOperationException(Denial);
    }
    // Server-owned canonical raid rules. Map, day/night and PMC/scav selection remain ordinary lobby choices.
    // Native bot mods (QB/SAIN/LB) still apply their headless-owned configuration.
    public static BotSettings Bots() => new() { IsScavWars = false, BotAmount = BotAmount.AsOnline };
    public static WavesSettings Waves() => new() { BotAmount = BotAmount.AsOnline, BotDifficulty = BotDifficulty.AsOnline, IsBosses = true, IsTaggedAndCursed = false };
    public static TimeAndWeatherSettings Weather() => new() { HourOfDay = -1, TimeFlowType = SPTarkov.Server.Core.Models.Enums.RaidSettings.TimeAndWeather.TimeFlowType.x1 };
    public static FikaCustomRaidSettings Custom() => new() { UseCustomWeather = false, DisableOverload = false, DisableLegStamina = false, DisableArmStamina = false };
    public static void Canonical(RaidSettings settings)
    {
        if (settings == null) throw new InvalidOperationException("FAIRPLAY_SETTINGS_MISSING");
        if (settings.ExtensionData is { Count: > 0 } || settings is GetRaidConfigurationRequestData derived && derived.ExtensionData is { Count: > 0 }) throw new InvalidOperationException("FAIRPLAY_UNKNOWN_RAID_EXTENSION");
        settings.MetabolismDisabled = false;
        settings.PlayersSpawnPlace = PlayersSpawnPlace.DifferentPlaces;
        settings.BotSettings = Bots(); settings.WavesSettings = Waves(); settings.TimeAndWeatherSettings = Weather();
        // Unknown extension data must be reviewed rather than silently passed to the headless.
    }
}

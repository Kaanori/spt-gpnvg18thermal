// ---------------------------------------------------------------
//  L3Harris GPNVG-18 Thermal goggles
//
//  Eine neue Brille: sieht aus wie die GPNVG-18 (Modell, vier Roehren,
//  Klappmechanik), zeigt aber das Waermebild der T-7.
//
//  Wie das geht:
//  Ob der Client Nachtsicht oder Waermebild rendert, haengt NUR am
//  Elternknoten der Vorlage (Assembly-CSharp, JsonTypes):
//    5a2c3a9486f774688b05e574 -> NightVision   (GPNVG-18)
//    5d21f59b6dbe99052b54ef83 -> ThermalVision (T-7)
//  Darum wird die T-7 geklont (alle Waermebildwerte stimmen damit
//  automatisch) und nur das Aussehen der GPNVG-18 draufgelegt:
//  Modell, Maske "Anvis", Gewicht, Konflikte. Die Maske nutzt bei
//  beiden Klassen dieselbe Aufzaehlung (NightVisionComponent.EMask).
//
//  Damit sie auch auf die Helme/Halterungen passt, wird die neue
//  Kennung ueberall eingetragen, wo die GPNVG-18 steht - einmal beim
//  Start und einmal bei der ersten Client-Anfrage, damit auch Helme
//  anderer Mods (WTT & Co.) erfasst werden, die erst spaeter laden.
// ---------------------------------------------------------------
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Common.Models.Logging;

using Pfad = System.IO.Path;

namespace GPNVG18Thermal;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.kaan.gpnvg18thermal";
    public string Name { get; init; } = "L3Harris GPNVG-18 Thermal goggles";
    public string Author { get; init; } = "Kaan";
    public List<string>? Contributors { get; init; } = new();
    public SemanticVersioning.Version Version { get; init; } = new(1, 0, 0);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = new();
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new();
    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}

/// <summary>Einstellungen aus config.json neben der DLL.</summary>
public sealed class Einstellungen
{
    public string Haendler { get; set; } = Brille.Jaeger;
    public int Stufe { get; set; } = 4;
    public double Preis { get; set; } = 17388000;
    public bool BeimHaendler { get; set; } = true;
}

public static class Brille
{
    // Feste Kennungen - duerfen sich nie aendern, sonst verschwinden
    // gekaufte Brillen aus dem Profil.
    public const string NeueId = "66f1c7a2b3d4e5f601a2b3c4";
    public const string AngebotId = "66f1c7a2b3d4e5f601a2b3c5";

    public const string Gpnvg = "5c0558060db834001b735271";
    public const string T7 = "5c110624d174af029e69734c";
    public const string WaermebildBasis = "5d21f59b6dbe99052b54ef83";
    public const string HandbuchSpezial = "5b5f749986f774094242f199";
    public const string Rubel = "5449016a4bdc2d6f028b456f";
    public const string Jaeger = "5c0647fdd443bc2504c2d371";

    private static readonly JsonSerializerOptions Leseweise = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static Einstellungen EinstellungenLesen(out string quelle)
    {
        var pfad = Pfad.Combine(
            Pfad.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "config.json");
        if (!File.Exists(pfad))
        {
            quelle = "Standardwerte (keine config.json)";
            return new Einstellungen();
        }
        try
        {
            quelle = "config.json";
            return JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(pfad), Leseweise)
                   ?? new Einstellungen();
        }
        catch (Exception ex)
        {
            quelle = "config.json unlesbar (" + ex.Message + ") - Standardwerte";
            return new Einstellungen();
        }
    }

    /// <summary>Traegt die neue Brille ueberall ein, wo die GPNVG-18 steht:
    /// in Slot-Filtern (Helme, Halterungen) und Konfliktlisten.
    /// Gibt zurueck, wie viele Stellen neu dazukamen.</summary>
    public static (int slots, int konflikte) VerweiseNachziehen(TemplateTable vorlagen)
    {
        var gpnvg = new MongoId(Gpnvg);
        var neu = new MongoId(NeueId);
        int slots = 0, konflikte = 0;

        foreach (var (_, vorlage) in vorlagen.Items)
        {
            var eig = vorlage.Properties;
            if (eig is null) continue;

            if (eig.Slots is not null)
            {
                foreach (var slot in eig.Slots)
                {
                    if (slot.Properties?.Filters is null) continue;
                    foreach (var filter in slot.Properties.Filters)
                    {
                        if (filter.Filter is not null && filter.Filter.Contains(gpnvg)
                            && filter.Filter.Add(neu))
                            slots++;
                    }
                }
            }

            if (eig.ConflictingItems is not null && eig.ConflictingItems.Contains(gpnvg)
                && eig.ConflictingItems.Add(neu))
                konflikte++;
        }

        return (slots, konflikte);
    }
}

/// <summary>Beim Serverstart: Brille anlegen, Haendlerangebot, Verweise.
/// Laeuft vor der Haendlerregistrierung, wie es Item-Mods unter 4.1.6 tun.</summary>
[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
public class BeimStart(
    ISptLogger<BeimStart> logger,
    TemplateTable vorlagen,
    TradersTable haendler,
    CustomItemService itemDienst) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken abbruch)
    {
        try
        {
            if (!vorlagen.Items.TryGetValue(new MongoId(Brille.Gpnvg), out var gpnvg)
                || gpnvg.Properties is null)
            {
                logger.Error("[GPNVG-18 Thermal] GPNVG-18 nicht in der Datenbank gefunden.");
                return Task.CompletedTask;
            }
            var g = gpnvg.Properties;

            var ergebnis = itemDienst.CreateItemFromClone(new NewItemFromCloneDetails
            {
                ItemTplToClone = new MongoId(Brille.T7),
                ParentId = new MongoId(Brille.WaermebildBasis),
                NewId = new MongoId(Brille.NeueId),
                NewItemName = "thermal_l3_gpnvg-18",
                OverrideProperties = new TemplateItemProperties
                {
                    // Aussehen und Handling der GPNVG-18
                    Prefab = new Prefab { Path = g.Prefab?.Path ?? "", Rcid = "" },
                    Mask = g.Mask,             // "Anvis" - vier Roehren
                    MaskSize = g.MaskSize,
                    Weight = g.Weight,
                    Width = g.Width,
                    Height = g.Height,
                    ItemSound = g.ItemSound,
                    ConflictingItems = new HashSet<MongoId>(g.ConflictingItems ?? new HashSet<MongoId>()),
                    CanSellOnRagfair = true
                },
                HandbookParentId = Brille.HandbuchSpezial,
                HandbookPriceRoubles = 480000,     // wie T-7
                FleaPriceRoubles = 15546666,       // wie T-7
                Locales = new Dictionary<string, LocaleDetails>
                {
                    ["en"] = new LocaleDetails
                    {
                        Name = "L3Harris GPNVG-18 Thermal goggles",
                        ShortName = "GPNVG-18 T",
                        Description = "A GPNVG-18 panoramic housing fitted with thermal imaging "
                                      + "modules taken from the Trijicon REAP-IR / T-7 line. "
                                      + "Four tubes, no night vision - pure thermal."
                    },
                    ["ge"] = new LocaleDetails
                    {
                        Name = "L3Harris GPNVG-18 Wärmebildbrille",
                        ShortName = "GPNVG-18 T",
                        Description = "Das Panorama-Gehäuse der GPNVG-18 mit den Wärmebildmodulen "
                                      + "der T-7. Vier Röhren, keine Restlichtverstärkung - "
                                      + "reines Wärmebild."
                    }
                }
            }, Assembly.GetExecutingAssembly());

            if (!ergebnis.Success)
            {
                logger.Error("[GPNVG-18 Thermal] Brille nicht angelegt: "
                             + string.Join("; ", ergebnis.Errors ?? new List<string>()));
                return Task.CompletedTask;
            }

            var cfg = Brille.EinstellungenLesen(out var quelle);
            var angebot = "kein Haendler";
            if (cfg.BeimHaendler)
                angebot = Anbieten(cfg);

            var (slots, konflikte) = Brille.VerweiseNachziehen(vorlagen);
            logger.Success($"[GPNVG-18 Thermal] Angelegt ({Brille.NeueId}). "
                           + $"Passt in {slots} Halterungen, {konflikte} Konflikte. "
                           + $"Angebot: {angebot}. Quelle: {quelle}");
        }
        catch (Exception ex)
        {
            logger.Error("[GPNVG-18 Thermal] " + ex);
        }

        return Task.CompletedTask;
    }

    private string Anbieten(Einstellungen cfg)
    {
        var trader = haendler.GetTrader(new MongoId(cfg.Haendler));
        if (trader?.Assort is null)
            return $"Haendler {cfg.Haendler} nicht gefunden";

        var angebotId = new MongoId(Brille.AngebotId);
        trader.Assort.Items.Add(new Item
        {
            Id = angebotId,
            Template = new MongoId(Brille.NeueId),
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd { UnlimitedCount = true, StackObjectsCount = 9999999 }
        });
        trader.Assort.BarterScheme[angebotId] = new List<List<BarterScheme>>
        {
            new() { new BarterScheme { Template = new MongoId(Brille.Rubel), Count = cfg.Preis } }
        };
        trader.Assort.LoyalLevelItems[angebotId] = cfg.Stufe;

        return $"{trader.Base?.Nickname ?? cfg.Haendler} Stufe {cfg.Stufe}, {cfg.Preis:N0} Rubel";
    }
}

/// <summary>Einmalig bei der ersten Client-Anfrage: Helme und Halterungen
/// nachziehen, die andere Mods erst nach uns eingehaengt haben.
/// Beantwortet selbst nie eine Anfrage.</summary>
[Injectable(InjectionType.Singleton)]
public class Nachziehen(
    ISptLogger<Nachziehen> logger,
    TemplateTable vorlagen) : IHttpListener
{
    private int _erledigt;

    public bool CanHandle(HttpContext context)
    {
        if (Interlocked.Exchange(ref _erledigt, 1) == 0)
        {
            try
            {
                var (slots, konflikte) = Brille.VerweiseNachziehen(vorlagen);
                if (slots + konflikte > 0)
                    logger.Success($"[GPNVG-18 Thermal] Nachgezogen: {slots} weitere Halterungen, "
                                   + $"{konflikte} Konflikte aus spaeter geladenen Mods.");
            }
            catch (Exception ex)
            {
                logger.Error("[GPNVG-18 Thermal] Nachziehen fehlgeschlagen: " + ex.Message);
            }
        }

        return false;   // immer: diese Mod beantwortet keine Anfragen
    }

    public Task HandleAsync(MongoId sessionId, HttpContext context,
                            CancellationToken abbruch = default)
        => Task.CompletedTask;
}

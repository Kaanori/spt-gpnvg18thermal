// ---------------------------------------------------------------
//  GPNVG-18 Thermal - Client-Teil
//
//  Das GPNVG-18-Modell traegt die Unity-Komponente
//  EFT.Visual.NightVisionDevice. Die schaltet nur das kleine Licht
//  an den Roehren, fragt dafuer aber fest nach der
//  NightVisionComponent des Items:
//
//      _nightVision = item.GetItemComponent<NightVisionComponent>();
//      _subscription = _nightVision.Togglable.OnChanged.Bind(...)
//
//  Unsere Brille ist eine ThermalVision und hat diese Komponente
//  nicht -> NullReferenceException -> der ganze Helm samt Brille
//  wird nicht gebaut (Log: "Failed to create item ... NFM HJELM").
//
//  Abhilfe: Fehlt die Nachtsicht-Komponente, haengen wir das Licht
//  an den Klappschalter der Waermebild-Komponente. Fuer alle echten
//  Nachtsichtgeraete laeuft das Original unveraendert.
//
//  Seit 1.1.0 zeichnet das Plugin ausserdem die Gehaeuse-Maske selbst
//  (an/aus und justierbar im F12-Menue) - siehe Maske.cs.
// ---------------------------------------------------------------
using BepInEx;
using EFT.InventoryLogic;
using EFT.Visual;
using HarmonyLib;

namespace GPNVG18ThermalClient
{
    [BepInPlugin("com.kaan.gpnvg18thermal.client", "GPNVG-18 Thermal (Client)", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Maske.Binden(Config, Logger);
            new Harmony("com.kaan.gpnvg18thermal.client").PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("NightVisionDevice patch and housing mask active");
        }
    }

    [HarmonyPatch(typeof(NightVisionDevice), nameof(NightVisionDevice.Init))]
    internal static class InitPatch
    {
        private static bool Prefix(NightVisionDevice __instance, Item item)
        {
            if (item.GetItemComponent<NightVisionComponent>() != null)
                return true;   // echtes Nachtsichtgeraet: Original laufen lassen

            var waerme = item.GetItemComponent<ThermalVisionComponent>();
            var licht = __instance._light;
            if (waerme == null)
            {
                // Weder noch - Licht aus und nichts abonnieren.
                if (licht != null) licht.enabled = false;
                __instance._subscription = null;
                return false;
            }

            var schalter = waerme.Togglable;
            __instance._subscription = schalter.OnChanged.Bind(() =>
            {
                if (licht != null) licht.enabled = schalter.On;
            });
            return false;
        }
    }

    [HarmonyPatch(typeof(NightVisionDevice), nameof(NightVisionDevice.Deinit))]
    internal static class DeinitPatch
    {
        // Ohne Abo gibt es nichts abzumelden - das Original wuerde
        // sonst null aufrufen.
        private static bool Prefix(NightVisionDevice __instance)
            => __instance._subscription != null;
    }
}

// ---------------------------------------------------------------
//  GPNVG-18 Thermal - Gehaeuse-Maske (seit 1.1.0)
//
//  Wer die Maske zeichnet:
//  Das Waermebild legt die Maske ueber die Spiel-Komponente
//  BSG.CameraEffects.TextureMask (ThermalVision.SetMaterialProperties:
//  TextureMask.Mask = MaskDescription.Mask, Size = MaskDescription.MaskSize).
//  Die Spiel-Komponente kennt nur Textur, Groesse (_InvMaskSize) und
//  Seitenverhaeltnis (_InvAspect = Hoehe/Breite der Textur,
//  _CameraAspect) - keinen Versatz, keinen Roehrenabstand.
//  BorkelRNVG tauscht beim Waermebild nur die Textur aus
//  (ThermalVisionSetMaterialPatch: mask.png aus Assets/Thermal/<name>/);
//  seine config.json kennt fuer Waermebild keine Masken-Werte.
//
//  Warum die alte GPNVG-Maske als Balken erschien:
//  Die mask.png aus BorkelRNVG/NVG/gpnvg ist fuer Borkels eigene
//  NACHTSICHT-Darstellung gemacht. Dort bestimmt lens.png, wo das
//  verstaerkte Bild liegt, und mask.png ist nur ein Ueberzug: um das
//  Gehaeuse herum Alpha 77 (= 30 % abgedunkelt), am Rand per Clamp bis
//  zum Bildschirmrand verlaengert. Beim Waermebild gibt es keine
//  lens.png-Stufe - das Waermebild fuellt den ganzen Schirm, und nur
//  der Ueberzug liegt darueber. Ergebnis: schwarze Gehaeuse-Streifen
//  oben/unten und zwischen den Roehren, ausserhalb davon das Waermebild
//  mit nur 30 % Abdunklung - "Balken" statt eines geschlossenen Rahmens.
//
//  Loesung hier: Wir erzeugen die Maskentextur selbst aus Werten im
//  F12-Menue und geben ihr das Seitenverhaeltnis der Kamera. Damit deckt
//  sie bei Size = 1 genau den Bildschirm ab - egal ob 16:9, 21:9 oder
//  16:10 -, die Roehren sitzen da, wo die Werte es sagen, und alles
//  ausserhalb der Roehren ist voll abgedeckt. Gilt mit und ohne
//  BorkelRNVG, nur fuer die eigene Brille und nur fuer die Kopf-Kamera.
// ---------------------------------------------------------------
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace GPNVG18ThermalClient
{
    internal static class Maske
    {
        public const string BrillenId = "66f1c7a2b3d4e5f601a2b3c4";

        // Grundform (Masseinheit: Bildschirmhoehe, Mitte = 0).
        // Abgeleitet aus BorkelRNVG LensLayouts/quadnods.json (Linsen in
        // einer 1520x640-Textur: Mitten x 0.2046/0.475/0.525/0.7954,
        // Radius 0.3875 der Hoehe) mal Borkels Standard-Maskengroesse 1.07
        // - so sieht die Waermebrille aus wie Borkels GPNVG-18-Nachtsicht.
        private const float GrundRadius = 0.3875f * 1.07f;                       // 0.4146
        private const float GrundAussen = (0.795394737f - 0.5f) * (1520f / 640f) * 1.07f; // 0.7507
        private const float GrundInnen = (0.525f - 0.5f) * (1520f / 640f) * 1.07f;       // 0.0635

        private const int TexturHoehe = 720;

        public static ConfigEntry<bool> An;
        public static ConfigEntry<float> Groesse;
        public static ConfigEntry<float> VersatzX;
        public static ConfigEntry<float> VersatzY;
        public static ConfigEntry<float> Abstand;
        public static ConfigEntry<float> RoehrenGroesse;
        public static ConfigEntry<float> Weichheit;
        public static ConfigEntry<float> Deckkraft;

        private static ManualLogSource _log;
        private static Texture2D _textur;
        private static Texture2D _leer;
        private static float _texturSeite = -1f;
        private static bool _neuZeichnen = true;
        private static bool _gemeldet;

        public static void Binden(ConfigFile cfg, ManualLogSource log)
        {
            _log = log;
            const string s = "GPNVG-18 Thermal - Mask";
            An = cfg.Bind(s, "1. Housing mask", true, new ConfigDescription(
                "Black GPNVG-18 housing around the four tubes. Off = thermal image on the whole screen, no housing."));
            Groesse = cfg.Bind(s, "2. Size", 1.0f, new ConfigDescription(
                "Scales the whole mask (tubes and spacing).", new AcceptableValueRange<float>(0.5f, 2.0f)));
            VersatzX = cfg.Bind(s, "3. Horizontal offset", 0.0f, new ConfigDescription(
                "Moves the tubes left (-) or right (+). Unit: screen height.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
            VersatzY = cfg.Bind(s, "4. Vertical offset", 0.0f, new ConfigDescription(
                "Moves the tubes down (-) or up (+). Unit: screen height.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
            Abstand = cfg.Bind(s, "5. Tube spacing", 1.0f, new ConfigDescription(
                "Distance between the tubes (1 = original GPNVG-18 layout).", new AcceptableValueRange<float>(0.5f, 1.5f)));
            RoehrenGroesse = cfg.Bind(s, "6. Tube size", 1.0f, new ConfigDescription(
                "Size of each tube opening, without changing the spacing.", new AcceptableValueRange<float>(0.5f, 1.5f)));
            Weichheit = cfg.Bind(s, "7. Edge softness", 0.015f, new ConfigDescription(
                "Blur of the tube edges (0 = hard edge). Unit: screen height.", new AcceptableValueRange<float>(0f, 0.1f)));
            Deckkraft = cfg.Bind(s, "8. Housing opacity", 1.0f, new ConfigDescription(
                "1 = solid black housing, lower values let the thermal image shine through.", new AcceptableValueRange<float>(0f, 1f)));

            // Jede Aenderung in diesem Abschnitt (F12, live) -> beim naechsten Bild neu zeichnen.
            cfg.SettingChanged += (_, a) =>
            {
                if (a.ChangedSetting.Definition.Section == s) _neuZeichnen = true;
            };
        }

        /// <summary>Nur die Kopf-Kamera und nur, wenn unsere Brille die
        /// aktive Waermebild-Brille ist. Waermebild-Visiere und die T-7
        /// bleiben unberuehrt.</summary>
        public static bool Zustaendig(ThermalVision tv)
        {
            if (tv == null || tv.TextureMask == null) return false;
            if (!ReferenceEquals(CameraManager.Instance.ThermalVision, tv)) return false;
            var id = Singleton<GameWorld>.Instance?.MainPlayer?.ThermalVisionObserver?.Component?.Item?.StringTemplateId;
            return id == BrillenId;
        }

        public static void Anwenden(ThermalVision tv)
        {
            var tm = tv.TextureMask;
            var kamera = tm._camera != null ? tm._camera : tm.GetComponent<Camera>();
            float seite = kamera != null && kamera.aspect > 0.1f ? kamera.aspect : 16f / 9f;

            Texture tex;
            if (!An.Value)
            {
                tex = Leer();
            }
            else
            {
                if (_neuZeichnen || _textur == null || Mathf.Abs(seite - _texturSeite) > 0.001f)
                    Zeichnen(seite);
                tex = _textur;
            }

            if (!_gemeldet)
            {
                _gemeldet = true;
                _log?.LogInfo($"Mask: {(An.Value ? "on" : "off")}, camera aspect {seite:0.000}, " +
                              $"item MaskSize {tv.ThermalVisionUtilities.MaskDescription.MaskSize:0.00}, Stretch {tm.Stretch}");
            }

            tm.Mask = tex;
            tm.Size = 1f;   // Textur hat Kamera-Seitenverhaeltnis -> deckt genau den Schirm
            tm.ApplySettings();
        }

        private static Texture2D Leer()
        {
            if (_leer != null) return _leer;
            _leer = new Texture2D(1, 1, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, name = "GPNVG18Thermal_NoMask" };
            _leer.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            _leer.Apply(false);
            return _leer;
        }

        private static void Zeichnen(float seite)
        {
            int h = TexturHoehe;
            int w = Mathf.Clamp(Mathf.RoundToInt(h * seite), 16, 4096);
            if (_textur == null || _textur.width != w || _textur.height != h)
            {
                if (_textur != null) Object.Destroy(_textur);
                _textur = new Texture2D(w, h, TextureFormat.ARGB32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "GPNVG18Thermal_Mask"
                };
            }

            float g = Groesse.Value;
            float r = GrundRadius * g * RoehrenGroesse.Value;
            float a = GrundAussen * g * Abstand.Value;
            float i = GrundInnen * g * Abstand.Value;
            float ox = VersatzX.Value, oy = VersatzY.Value;
            float[] mx = { ox - a, ox - i, ox + i, ox + a };
            // halbe Breite des weichen Randes, mindestens ein halbes Texel
            float weich = Mathf.Max(Weichheit.Value * 0.5f, 0.5f / h);
            float deck = Mathf.Clamp01(Deckkraft.Value);
            byte voll = (byte)Mathf.RoundToInt(deck * 255f);

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                // Zeile 0 der Textur = unterer Bildrand
                float dy = (y + 0.5f) / h - 0.5f - oy;
                int zeile = y * w;
                if (Mathf.Abs(dy) >= r + weich)
                {
                    for (int x = 0; x < w; x++) px[zeile + x] = new Color32(0, 0, 0, voll);
                    continue;
                }
                float dy2 = dy * dy;
                for (int x = 0; x < w; x++)
                {
                    float fx = ((x + 0.5f) / w - 0.5f) * seite;
                    float frei = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        float dx = fx - mx[k];
                        float d = Mathf.Sqrt(dx * dx + dy2) - r;
                        if (d >= weich) continue;
                        float t = Mathf.Clamp01((d + weich) / (2f * weich));
                        float o = 1f - t * t * (3f - 2f * t);
                        if (o > frei) frei = o;
                    }
                    px[zeile + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(deck * (1f - frei) * 255f));
                }
            }
            _textur.SetPixels32(px);
            _textur.Apply(false);
            _texturSeite = seite;
            _neuZeichnen = false;
        }
    }

    [HarmonyPatch(typeof(ThermalVision), nameof(ThermalVision.SetMaterialProperties))]
    internal static class MaskePatch
    {
        // Postfix: laeuft nach dem Original (und damit nach Borkels Prefix,
        // der nur die Textur in MaskDescription tauscht). Wir setzen die
        // TextureMask danach neu - die Reihenfolge der Patches ist egal.
        private static void Postfix(ThermalVision __instance)
        {
            if (Maske.Zustaendig(__instance))
                Maske.Anwenden(__instance);
        }
    }
}

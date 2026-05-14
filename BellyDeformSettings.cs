using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SVSPregnancy
{
    // ── Vertex-mode parameters ───────────────────────────────────────────
    public class VtxSettings
    {
        // All length/offset parameters below are NORMALISED by the character's
        // pelvis-to-spine bone distance (boneLen).  1.0 = one boneLen unit.
        // This makes the same settings produce proportionally identical results
        // on characters of any height or body scale.

        /// <summary>Lerp between pelvis (0) and spine (1) to place the belly center.</summary>
        [JsonPropertyName("spineLerpT")]      public float SpineLerpT    { get; set; } = 0.50f;
        /// <summary>Global size multiplier applied to all radii (1 = normal).</summary>
        [JsonPropertyName("inflationSize")]   public float InflationSize { get; set; } = 4.000f;
        /// <summary>Additional up-axis offset of the belly centre, normalised by boneLen.</summary>
        [JsonPropertyName("moveY")]           public float MoveY         { get; set; } = -0.656f;
        /// <summary>Additional forward-axis offset of the belly centre, normalised by boneLen.</summary>
        [JsonPropertyName("moveZ")]           public float MoveZ         { get; set; } = -1.125f;
        /// <summary>Ellipsoid half-radius left/right, normalised by boneLen.</summary>
        [JsonPropertyName("radiusSide")]      public float RadiusSide    { get; set; } = 2.025f;
        /// <summary>Ellipsoid half-radius forward (front face), normalised by boneLen.</summary>
        [JsonPropertyName("radiusFront")]     public float RadiusFront   { get; set; } = 2.311f;
        /// <summary>Ellipsoid half-radius backward (back face), normalised by boneLen.</summary>
        [JsonPropertyName("radiusBack")]      public float RadiusBack    { get; set; } = 0.000f;
        /// <summary>Ellipsoid half-radius upward, normalised by boneLen.</summary>
        [JsonPropertyName("radiusUp")]        public float RadiusUp      { get; set; } = 2.704f;
        /// <summary>Ellipsoid half-radius downward, normalised by boneLen.</summary>
        [JsonPropertyName("radiusDown")]      public float RadiusDown    { get; set; } = 1.870f;
        /// <summary>Extra scale along the belly's right axis.</summary>
        [JsonPropertyName("stretchX")]        public float StretchX      { get; set; } = 0.000f;
        /// <summary>Extra scale along the belly's up axis.</summary>
        [JsonPropertyName("stretchY")]        public float StretchY      { get; set; } = 0.000f;
        /// <summary>Extra scale along the belly's forward axis.</summary>
        [JsonPropertyName("stretchZ")]        public float StretchZ      { get; set; } = 0.125f;
        /// <summary>Shift the deformed belly upward (+) or downward (−), normalised by boneLen.</summary>
        [JsonPropertyName("shiftY")]          public float ShiftY        { get; set; } = 0.000f;
        /// <summary>Shift the deformed belly forward (+) or backward (−), normalised by boneLen.</summary>
        [JsonPropertyName("shiftZ")]          public float ShiftZ        { get; set; } = 0.000f;
        /// <summary>Gravity-style downward pull on the front face. 0 = round, 1 = heavy sag.</summary>
        [JsonPropertyName("drop")]            public float Drop          { get; set; } = -0.500f;
        /// <summary>Vertical taper: >0 = narrower at top, <0 = wider at top.</summary>
        [JsonPropertyName("taperY")]          public float TaperY        { get; set; } = 0.000f;
        /// <summary>Depth taper: >0 = narrower at front, <0 = wider at front.</summary>
        [JsonPropertyName("taperZ")]          public float TaperZ        { get; set; } = 0.266f;
        /// <summary>Blend toward sphere (positive) or sharpen ellipsoid (negative). Range [-1, 1].</summary>
        [JsonPropertyName("roundness")]       public float Roundness     { get; set; } = 0.219f;
        /// <summary>
        /// Width of the smooth falloff zone at the belly boundary.
        /// 0 = falloff only at ellipsoid surface (sharpest edge).
        /// 1 = falloff begins at 50% of the radius (very soft, feathered edge).
        /// </summary>
        [JsonPropertyName("edgeSmooth")]      public float EdgeSmooth    { get; set; } = 1.000f;
        /// <summary>Depth of the fat-fold crease under the belly. 0 = none.</summary>
        [JsonPropertyName("fatFold")]         public float FatFold       { get; set; } = 0.000f;
        /// <summary>Vertical position of the fat-fold, normalised by RadiusDown (0 = center, 1 = bottom).</summary>
        [JsonPropertyName("fatFoldHeight")]   public float FatFoldHeight { get; set; } = 0.000f;
        /// <summary>Width of the fat-fold Gaussian, normalised by RadiusDown.</summary>
        [JsonPropertyName("fatFoldGap")]      public float FatFoldGap    { get; set; } = 0.050f;

        // ── Back-face limiter ─────────────────────────────────────────────
        /// <summary>
        /// Normalised depth of the back-cut plane (0=disabled, 1=cut at belly center).
        /// Vertices behind  fwD = -BackLimit*rB  have their deformation reduced.
        /// </summary>
        [JsonPropertyName("backLimit")]    public float BackLimit    { get; set; } = 0.00f;
        /// <summary>How much to reduce deformation in the back zone. 0=none, 1=full cut.</summary>
        [JsonPropertyName("backStrength")] public float BackStrength { get; set; } = 0.00f;
        /// <summary>
        /// Width of the smooth transition zone, normalised by rB.
        /// Points within this distance from the cut plane are blended gradually.
        /// </summary>
        [JsonPropertyName("backSmooth")]   public float BackSmooth   { get; set; } = 0.00f;

        // ── Breast guard ──────────────────────────────────────────────────
        /// <summary>
        /// Strength of the breast-guard restore step.
        /// Vertices weighted to breast bones (mune/bust) are lerped back toward their
        /// original positions by (boneWeight × 4 × BreastGuardStrength), clamped 0..1.
        /// 0 = no guard; 1 = default (25%+ breast-bone weight → fully restored).
        /// Values above 1 guard more aggressively.
        /// </summary>
        [JsonPropertyName("breastGuard")] public float BreastGuardStrength { get; set; } = 1.0f;
    }

    // ── JSON data container (internal) ──────────────────────────────────
    internal class BellySettingsData
    {
        /// <summary>
        /// Config format version.  0 (absent) = old absolute-metre radii.
        /// 2 = current normalised-by-boneLen radii.
        /// If version &lt; 2 the Vtx block is ignored and defaults are used instead.
        /// </summary>
        [JsonPropertyName("version")]  public int         Version  { get; set; } = 0;
        [JsonPropertyName("startDay")] public int         StartDay { get; set; } = 40;
        [JsonPropertyName("vtx")]      public VtxSettings Vtx      { get; set; } = new();
    }

    // ── Static settings store ────────────────────────────────────────────
    public static class BellyDeformSettings
    {
        private static readonly string FilePath = Path.Combine(
            BepInEx.Paths.ConfigPath, "SVSPregnancy_belly.json");

        public static int         StartDay { get; private set; } = 40;
        public static VtxSettings Vtx      { get; private set; } = new VtxSettings();

        // ── Reset ─────────────────────────────────────────────────────────
        public static void ResetToDefaults()
        {
            StartDay = 40;
            Vtx      = new VtxSettings();
        }

        // ── Delete on-disk config (e.g. to remove an old-format file) ────
        public static void DeleteConfigFile()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Belly")
                    .LogWarning("[SVSPregnancy] DeleteConfigFile failed: " + e.Message);
            }
        }

        // ── Live preview (in-memory update, no file write) ───────────────
        /// <summary>Apply settings in-memory without touching the JSON file (for live UI preview).</summary>
        public static void SetLive(int startDay, VtxSettings vtx)
        {
            StartDay = startDay;
            Vtx      = vtx;
        }

        // ── Save ─────────────────────────────────────────────────────────
        public static void Save(int startDay, VtxSettings vtx)
        {
            StartDay = startDay;
            Vtx      = vtx;
            try
            {
                var data = new BellySettingsData { Version = 2, StartDay = StartDay, Vtx = Vtx };
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(data,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Belly")
                    .LogError("[SVSPregnancy] BellyDeformSettings.Save failed: " + e.Message);
            }
        }

        // ── Load ─────────────────────────────────────────────────────────
        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                var log  = BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Belly");
                var data = JsonSerializer.Deserialize<BellySettingsData>(
                               File.ReadAllText(FilePath));
                if (data == null) return;

                StartDay = data.StartDay;

                if (data.Version < 2)
                {
                    // Old config used absolute metre radii (e.g. radiusSide ≈ 0.18 m).
                    // The current code multiplies every radius by boneLen (≈ 0.15 m), so
                    // the old values would produce a nearly invisible belly (~2.7 cm radius).
                    // Safe fix: discard the old Vtx block and keep defaults.
                    log.LogWarning(
                        "[SVSPregnancy] Old config format (v<2, absolute radii). " +
                        "Vtx settings reset to defaults. Re-save from the UI to persist.");
                }
                else if (data.Vtx != null)
                {
                    Vtx = data.Vtx;
                }
            }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Belly")
                    .LogError("[SVSPregnancy] BellyDeformSettings.Load failed: " + e.Message);
            }
        }
    }
}

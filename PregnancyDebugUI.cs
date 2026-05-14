using Il2CppInterop.Runtime;
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SVSPregnancy
{
    public class PregnancyDebugUI : MonoBehaviour
    {
        public PregnancyDebugUI(IntPtr ptr) : base(ptr) { }

        private bool  _show       = false;
        private Rect  _windowRect = new Rect(20, 80, 540, 560);
        private Vector2 _charScroll = Vector2.zero;
        private int   _selectedCharaId = -1;

        // ── Belly-settings panel ──────────────────────────────────────────
        private bool     _showBellySettings = false;
        private Vector2  _vtxScroll         = Vector2.zero;
        private string   _bellyMsg          = "";

        // Text-field buffers — 26 entries, one per vtx param.
        // Indices:
        //  0  SpineLerpT    1  InflationSize  2  MoveY       3  MoveZ
        //  4  RadiusSide    5  RadiusFront    6  RadiusBack   7  RadiusUp    8  RadiusDown
        //  9  StretchX      10 StretchY       11 StretchZ
        // 12  ShiftY        13 ShiftZ         14 Drop
        // 15  TaperY        16 TaperZ         17 Roundness   18 EdgeSmooth
        // 19  FatFold       20 FatFoldHeight  21 FatFoldGap
        // 22  BackLimit     23 BackStrength   24 BackSmooth
        // 25  BreastGuard
        private string[] _vtxBuf      = null;
        private string   _startDayBuf = "40";
        private bool     _vtxBufInited = false;

        // Lazily created 1×1 white texture for the progress bar fill
        private static Texture2D _whiteTex;
        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                    UnityEngine.Object.DontDestroyOnLoad(_whiteTex);
                }
                return _whiteTex;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(PregnancyPlugin.DebugUIKey.Value))
                _show = !_show;
        }

        void OnGUI()
        {
            if (!_show) return;
            _windowRect = GUILayout.Window(
                98765, _windowRect,
                (GUI.WindowFunction)((Action<int>)DrawWindow),
                "SVSPregnancy Debug");
        }

        private void DrawWindow(int id)
        {
            var worldCtrl = PregnancyPlugin._worldController;
            if (worldCtrl == null || !worldCtrl._inited)
            {
                GUILayout.Label("World controller not initialized yet.");
                GUI.DragWindow();
                return;
            }

            var females = worldCtrl._PregnancyCharaControllers
                .Select(p => p.ToObject<PregnancyCharaController>())
                .Where(c => c != null && c._pregnancyInfo != null && c._pregnancyInfo._sex == 1)
                .ToList();

            GUILayout.BeginHorizontal();

            // ── Left panel: character list ────────────────────────────────
            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("Characters:");
            _charScroll = GUILayout.BeginScrollView(_charScroll, GUILayout.Height(200));
            foreach (var ctrl in females)
            {
                string label    = GetName(ctrl);
                bool   selected = ctrl._charaId == _selectedCharaId;
                var    style    = selected ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button(label, style))
                    _selectedCharaId = ctrl._charaId;
            }
            if (females.Count == 0)
                GUILayout.Label("(none)");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // ── Right panel: selected character info ──────────────────────
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            var sel = females.FirstOrDefault(c => c._charaId == _selectedCharaId);
            if (sel == null && females.Count > 0)
            {
                sel = females[0];
                _selectedCharaId = sel._charaId;
            }

            if (sel != null)
                DrawCharaInfo(sel);
            else
                GUILayout.Label("No female characters in world controller.");

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // ── Apply / Reset buttons ─────────────────────────────────────
            GUILayout.Space(4);
            if (BellyVertexMorph.Paused)
                GUILayout.Label("⚠ Deform paused — click Apply to resume");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Belly Deform (All)"))
                ApplyBellyAll();
            if (GUILayout.Button("Reset Deform (All)"))
                ResetBellyAll();
            if (GUILayout.Button("Dump Mesh Info (Log)"))
                DumpMeshInfoAll();
            GUILayout.EndHorizontal();

            // ── Belly deform settings (collapsible) ───────────────────────
            GUILayout.Space(6);
            var toggleLabel = _showBellySettings ? "▼ Belly Deform Settings" : "▶ Belly Deform Settings";
            if (GUILayout.Button(toggleLabel))
            {
                _showBellySettings = !_showBellySettings;
                if (_showBellySettings) EnsureVtxBuf();
            }

            if (_showBellySettings)
                DrawVtxSettings();

            GUI.DragWindow();
        }

        // ─────────────────────────────────────────────────────────────────
        // Character panel
        // ─────────────────────────────────────────────────────────────────
        private void DrawCharaInfo(PregnancyCharaController ctrl)
        {
            var info = ctrl._pregnancyInfo;

            GUILayout.Label($"[{GetName(ctrl)}]  charaId = {ctrl._charaId}");
            GUILayout.Space(4);

            if (info.IsPregnant)
            {
                float progress = Mathf.Clamp01((float)info._day / info._currentMaximalPregnantDays);
                int   stage    = ctrl.CheckBabySize();
                string stageStr = stage switch
                {
                    0 => "0 (None)", 1 => "1 (S)", 2 => "2 (M)",
                    3 => "3 (L)",    4 => "4 (XL)", _ => stage.ToString()
                };

                GUILayout.Label(
                    $"Pregnant  Day {info._day} / {info._currentMaximalPregnantDays}  " +
                    $"({(int)(progress * 100)}%)   Stage {stageStr}");

                Rect barRect = GUILayoutUtility.GetRect(
                    GUIContent.none, GUIStyle.none,
                    GUILayout.Height(16), GUILayout.ExpandWidth(true));
                GUI.Box(barRect, GUIContent.none);
                if (progress > 0f)
                {
                    var fill = new Rect(barRect.x + 1, barRect.y + 1,
                                        (barRect.width - 2) * progress, barRect.height - 2);
                    var prev = GUI.color;
                    GUI.color = Color.Lerp(Color.green, Color.red, progress);
                    GUI.DrawTexture(fill, WhiteTex);
                    GUI.color = prev;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Day:", GUILayout.Width(34));
                int newDay = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                    info._day, 0f, info._currentMaximalPregnantDays));
                GUILayout.Label(newDay.ToString(), GUILayout.Width(40));
                GUILayout.EndHorizontal();
                if (newDay != info._day)
                    info._day = newDay;

                if (info.IsCoolingdown)
                {
                    GUILayout.Space(4);
                    GUILayout.Label($"Post-birth cooldown: {info._cooldown} days remaining");
                }
            }
            else if (info.IsCoolingdown)
            {
                GUILayout.Label($"Post-birth cooldown: {info._cooldown} days remaining");
                GUILayout.Space(4);
                if (GUILayout.Button("Reset Cooldown"))
                    info._cooldown = 0;
            }
            else
            {
                GUILayout.Label("Not pregnant.");
                GUILayout.Space(4);
                if (GUILayout.Button("Force Conceive (Debug)"))
                    ctrl.Conceive("Debug", "Debug", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Vertex morph settings panel — text fields + sliders
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Populate _vtxBuf from current BellyDeformSettings (once).</summary>
        private void EnsureVtxBuf()
        {
            if (_vtxBufInited) return;
            SyncBufFromSettings();
            _vtxBufInited = true;
        }

        /// <summary>Copy current settings into the string buffers.</summary>
        private void SyncBufFromSettings()
        {
            var s = BellyDeformSettings.Vtx;
            _startDayBuf = BellyDeformSettings.StartDay.ToString();
            _vtxBuf = new string[26];
            _vtxBuf[0]  = Fmt(s.SpineLerpT);    _vtxBuf[1]  = Fmt(s.InflationSize);
            _vtxBuf[2]  = Fmt(s.MoveY);          _vtxBuf[3]  = Fmt(s.MoveZ);
            _vtxBuf[4]  = Fmt(s.RadiusSide);     _vtxBuf[5]  = Fmt(s.RadiusFront);
            _vtxBuf[6]  = Fmt(s.RadiusBack);     _vtxBuf[7]  = Fmt(s.RadiusUp);
            _vtxBuf[8]  = Fmt(s.RadiusDown);
            _vtxBuf[9]  = Fmt(s.StretchX);       _vtxBuf[10] = Fmt(s.StretchY);
            _vtxBuf[11] = Fmt(s.StretchZ);
            _vtxBuf[12] = Fmt(s.ShiftY);         _vtxBuf[13] = Fmt(s.ShiftZ);
            _vtxBuf[14] = Fmt(s.Drop);
            _vtxBuf[15] = Fmt(s.TaperY);         _vtxBuf[16] = Fmt(s.TaperZ);
            _vtxBuf[17] = Fmt(s.Roundness);      _vtxBuf[18] = Fmt(s.EdgeSmooth);
            _vtxBuf[19] = Fmt(s.FatFold);        _vtxBuf[20] = Fmt(s.FatFoldHeight);
            _vtxBuf[21] = Fmt(s.FatFoldGap);
            _vtxBuf[22] = Fmt(s.BackLimit);      _vtxBuf[23] = Fmt(s.BackStrength);
            _vtxBuf[24] = Fmt(s.BackSmooth);
            _vtxBuf[25] = Fmt(s.BreastGuardStrength);
        }

        /// <summary>Parse the string buffers back into a VtxSettings object.</summary>
        private VtxSettings BuildVtxFromBuf() => new VtxSettings
        {
            SpineLerpT    = ParseF(_vtxBuf[0],   0.500f),
            InflationSize = ParseF(_vtxBuf[1],   4.000f),
            MoveY         = ParseF(_vtxBuf[2],  -0.656f),
            MoveZ         = ParseF(_vtxBuf[3],  -1.125f),
            RadiusSide    = ParseF(_vtxBuf[4],   2.025f),
            RadiusFront   = ParseF(_vtxBuf[5],   2.311f),
            RadiusBack    = ParseF(_vtxBuf[6],   0.000f),
            RadiusUp      = ParseF(_vtxBuf[7],   2.704f),
            RadiusDown    = ParseF(_vtxBuf[8],   1.870f),
            StretchX      = ParseF(_vtxBuf[9],   0.000f),
            StretchY      = ParseF(_vtxBuf[10],  0.000f),
            StretchZ      = ParseF(_vtxBuf[11],  0.125f),
            ShiftY        = ParseF(_vtxBuf[12],  0.000f),
            ShiftZ        = ParseF(_vtxBuf[13],  0.000f),
            Drop          = ParseF(_vtxBuf[14], -0.500f),
            TaperY        = ParseF(_vtxBuf[15],  0.000f),
            TaperZ        = ParseF(_vtxBuf[16],  0.266f),
            Roundness     = ParseF(_vtxBuf[17],  0.219f),
            EdgeSmooth    = ParseF(_vtxBuf[18],  1.000f),
            FatFold       = ParseF(_vtxBuf[19],  0.000f),
            FatFoldHeight = ParseF(_vtxBuf[20],  0.000f),
            FatFoldGap    = ParseF(_vtxBuf[21],  0.050f),
            BackLimit             = ParseF(_vtxBuf[22],  0.000f),
            BackStrength          = ParseF(_vtxBuf[23],  0.000f),
            BackSmooth            = ParseF(_vtxBuf[24],  0.000f),
            BreastGuardStrength   = ParseF(_vtxBuf[25],  1.000f),
        };

        private static float ParseF(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (float.TryParse(s, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float v)) return v;
            return fallback;
        }

        private static string Fmt(float v) =>
            v.ToString("F3", CultureInfo.InvariantCulture);

        private void DrawVtxSettings()
        {
            EnsureVtxBuf();
            GUILayout.Space(4);

            // ── Status line (bone-finding / boneLen / last rate) ─────────
            {
                string status = _selectedCharaId >= 0
                    ? BellyVertexMorph.GetStatusLine(_selectedCharaId)
                    : "select a character above";
                GUILayout.Label("State: " + status);
            }

            bool changed = false;

            // ── Start Day ────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Start Day:", GUILayout.Width(110));
            string sdNext = GUILayout.TextField(_startDayBuf, GUILayout.Width(55));
            if (sdNext != _startDayBuf) { _startDayBuf = sdNext; changed = true; }
            if (int.TryParse(_startDayBuf, out int sdParsed))
            {
                int sdSlid = Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(sdParsed, 0f, 280f, GUILayout.Width(140)));
                if (sdSlid != sdParsed) { _startDayBuf = sdSlid.ToString(); changed = true; }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _vtxScroll = GUILayout.BeginScrollView(_vtxScroll, GUILayout.Height(360));

            GUILayout.Label("── Center ──");
            changed |= VR("Spine Lerp T",   0,   0f,    1f);
            changed |= VR("Move Y",         2,  -2f,    2f);   // ×boneLen
            changed |= VR("Move Z",         3,  -2f,    2f);   // ×boneLen

            GUILayout.Space(4);
            GUILayout.Label("── Size  (×boneLen) ──");
            changed |= VR("Inflation Size", 1,   0.1f,  4.0f);
            changed |= VR("Radius Side",    4,   0.05f, 4.0f);
            changed |= VR("Radius Front",   5,   0.05f, 4.0f);
            changed |= VR("Radius Back",    6,   0.05f, 4.0f);
            changed |= VR("Radius Up",      7,   0.05f, 4.0f);
            changed |= VR("Radius Down",    8,   0.05f, 4.0f);

            GUILayout.Space(4);
            GUILayout.Label("── Shape ──");
            changed |= VR("Stretch X",      9,  -1f,   1f);
            changed |= VR("Stretch Y",     10,  -1f,   1f);
            changed |= VR("Stretch Z",     11,  -1f,   1f);
            changed |= VR("Shift Y",       12,  -1.5f, 1.5f); // ×boneLen
            changed |= VR("Shift Z",       13,  -1.5f, 1.5f); // ×boneLen
            changed |= VR("Drop",          14,  -0.5f, 1.5f);
            changed |= VR("Taper Y",       15,  -1f,   1f);
            changed |= VR("Taper Z",       16,  -1f,   1f);
            changed |= VR("Roundness",     17,  -1f,   1f);
            // 0 = sharpest boundary; 1 = very soft, feathered edge
            changed |= VR("Edge Smooth",   18,   0f,   1f);

            GUILayout.Space(4);
            GUILayout.Label("── Fat Fold ──");
            changed |= VR("Fat Fold",      19,   0f,   1f);
            changed |= VR("Fold Height",   20,  -1f,   1f);
            changed |= VR("Fold Gap",      21,   0.01f,0.5f);

            GUILayout.Space(4);
            GUILayout.Label("── Back Limit ──");
            changed |= VR("Back Limit",    22,   0f,   1f);
            changed |= VR("Back Strength", 23,   0f,   1f);
            changed |= VR("Back Smooth",   24,   0f,   1f);

            GUILayout.Space(4);
            GUILayout.Label("── Breast Guard ──────────────────");
            changed |= VR("Breast Guard",  25,   0f,   2f);

            GUILayout.EndScrollView();

            // Live preview on any change
            if (changed)
            {
                int sd = int.TryParse(_startDayBuf, out int sdv) ? sdv : 40;
                BellyDeformSettings.SetLive(sd, BuildVtxFromBuf());
                BellyVertexMorph.InvalidateAll();
                _bellyMsg = "";
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to File"))
            {
                int sd = int.TryParse(_startDayBuf, out int sdv) ? sdv : 40;
                BellyDeformSettings.Save(sd, BuildVtxFromBuf());
                BellyVertexMorph.InvalidateAll();
                _bellyMsg = "Saved!";
            }
            if (GUILayout.Button("Reset Defaults"))
            {
                // Also delete the JSON so any old-format file can't interfere on next load.
                BellyDeformSettings.DeleteConfigFile();
                BellyDeformSettings.ResetToDefaults();
                SyncBufFromSettings();
                BellyVertexMorph.InvalidateAll();
                _bellyMsg = "Reset to defaults (config file deleted). Click Save to write new file.";
            }
            GUILayout.Label(_bellyMsg, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draw one parameter row: label | text-field | slider.
        /// The text field is the authority — typing updates the value immediately.
        /// The slider syncs to the parsed text value; dragging it updates the text field.
        /// Returns true when either input changed this frame.
        /// </summary>
        private bool VR(string label, int idx, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));

            // Text field
            string prev = _vtxBuf[idx];
            string next = GUILayout.TextField(prev, GUILayout.Width(60));
            bool changed = false;
            if (next != prev) { _vtxBuf[idx] = next; changed = true; }

            // Slider — clamp the parsed value into [min, max] before passing it to
            // HorizontalSlider.  Without this, a partially-typed or empty field yields
            // ParseF(...)=0 which may be below min (e.g. RadiusSide min=0.05).
            // Unity IMGUI then clamps the slider output to min, so
            // !Approximately(min, 0) is TRUE every frame → spurious changed=true every
            // frame → InvalidateAll() every frame → full ApplySMR + log spam → freeze.
            float cur     = ParseF(_vtxBuf[idx], 0f);
            float curSlid = Mathf.Clamp(cur, min, max);
            float slid    = GUILayout.HorizontalSlider(curSlid, min, max, GUILayout.Width(140));
            if (!Mathf.Approximately(slid, curSlid))
            {
                _vtxBuf[idx] = Fmt(slid);
                changed = true;
            }

            GUILayout.EndHorizontal();
            return changed;
        }

        private static string GetName(PregnancyCharaController ctrl)
        {
            try { return ctrl._chara.parameter.lastname + " " + ctrl._chara.parameter.firstname; }
            catch { return $"ID:{ctrl._charaId}"; }
        }

        private static void ApplyBellyAll()
        {
            // Un-pause first so LateUpdate hooks resume, then force an immediate re-apply.
            BellyVertexMorph.Paused = false;
            BellyVertexMorph.InvalidateAll();
            var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PregnancyHumanController>());
            foreach (var obj in all)
            {
                var hc = obj.TryCast<PregnancyHumanController>();
                if (hc == null) continue;
                try { if (hc.GetSex() != 1) continue; hc.ModifyBelly(); }
                catch { }
            }
        }

        private static void ResetBellyAll()
        {
            // 1. Undo every applied deformation and clear all cached state.
            BellyVertexMorph.ForgetAll();
            // 2. Freeze — LateUpdate hooks will not re-apply until Apply is clicked.
            BellyVertexMorph.Paused = true;
        }

        private static void DumpMeshInfoAll()
        {
            var log = BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Dump");
            log.LogInfo("[Dump] ======== DumpMeshInfoAll triggered ========");

            var allHC = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PregnancyHumanController>());
            log.LogInfo($"[Dump] PregnancyHumanController instances: {allHC.Count}");
            foreach (var obj in allHC)
            {
                var hc = obj.TryCast<PregnancyHumanController>();
                if (hc == null) continue;
                try
                {
                    log.LogInfo($"[Dump] PHC id={hc._charaId} sex={hc.GetSex()} inited={hc._inited} humanPtr={hc._humanPtr}");
                    if (hc._human != null)
                        BellyVertexMorph.DumpInfo(hc._human, hc._charaId);
                }
                catch (Exception e) { log.LogInfo("[Dump] PHC error: " + e.Message); }
            }

            log.LogInfo("[Dump] ======== end ========");
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Logging;
using Character;

namespace SVSPregnancy
{
    /// <summary>
    /// Vertex-level belly deformation for SVS IL2CPP.
    ///
    /// Mechanisms (mirrored from PregnancyPlus source code analysis):
    ///   1. Bind-pose local space computation  — pose-independent
    ///   2. Bone-weight vertex filtering        — excludes legs, chest, arms
    ///   3. LowerBodyRestoreMask                — leg-dominated vertices skipped
    ///   4. RoundToSides Z-distance falloff     — PP's AnimationCurve (SmoothStep approx)
    ///   5. ReduceRibStretchingZ                — limits chest push-forward
    ///   6. RecalculateNormals + Tangents       — correct lighting after deformation
    /// </summary>
    internal static class BellyVertexMorph
    {
        // ── SVS/KK-family bone names ──────────────────────────────────────
        private static readonly string[] PelvisBones =
            { "cf_j_waist01", "cf_s_waist01", "cf_j_hips01" };
        private static readonly string[] SpineBones =
            { "cf_j_spine01", "cf_s_spine01", "cf_j_spine02" };
        private static readonly string[] LThighBones =
            { "cf_j_thigh00_L", "cf_s_thigh01_L", "cf_j_leg01_L" };
        private static readonly string[] RThighBones =
            { "cf_j_thigh00_R", "cf_s_thigh01_R", "cf_j_leg01_R" };

        private static readonly string[] FaceKeywords =
            { "head", "face", "eye", "mayu", "tooth", "teeth",
              "tongue", "lip", "nose", "ear", "hair", "o_acs_", "_acs_" };

        // ── Per-character runtime cache ───────────────────────────────────
        private class CharaState
        {
            public SkinnedMeshRenderer SMR;
            // Transform refs (reliable — from Human hierarchy)
            public Transform PelvisTf, SpineTf, LThighTf, RThighTf;
            // Indices in smr.bones[] for bindpose lookup (-1 = not found)
            public int PelvisIdx = -1, SpineIdx  = -1;
            public int LThighIdx = -1, RThighIdx = -1;
            // Bone index sets for PP-style vertex weight filtering
            public HashSet<int> BellyBoneIdxSet;  // waist/spine bones
            public HashSet<int> LegBoneIdxSet;    // thigh/leg bones
            public bool BonesFound;
            public LocalFrame Frame;
            public bool       FrameValid;
            public float      LastAppliedRate  = float.NaN;
            public float      LastLoggedRate   = float.NaN;   // rate at last LogInfo — avoids log spam from UI InvalidateAll
        }

        // ── Mesh deformation record ───────────────────────────────────────
        private class MeshRecord
        {
            public Mesh      Mesh;
            public Vector3[] OrigVerts;        // bind-pose baseline
            public Vector3[] LastNewV;
            public bool[]    BellyMask;        // per-vertex bone-weight pass/fail (null = no filter)
            public float[]   BreastWeights;    // per-vertex breast-bone weight sum 0..1 (null = not computed / no breast bones found)
            public int       AppliedSig;
            public int       LastDeformedCount = -1;  // vertex count from last ApplySMR — used to suppress repeated log lines
        }

        private static readonly Dictionary<int, CharaState>       _state   = new();
        private static readonly Dictionary<int, List<MeshRecord>> _records = new();

        private struct LocalFrame
        {
            public Vector3 Center, Up, Fwd, Right;
            public float   BoneLen;
            public bool    BindPoseBased;
        }

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Vtx");

        // ── Global deform pause flag ──────────────────────────────────────
        /// <summary>
        /// When true, Apply() is a no-op and LateUpdate hooks skip deformation.
        /// Set via the Debug UI "Reset Deform" button; cleared by "Apply Belly Deform".
        /// </summary>
        public static bool Paused = false;

        // ── Public API ────────────────────────────────────────────────────

        public static void Apply(Human human, int charaId, float rate)
        {
            if (human == null) return;
            rate = Mathf.Clamp01(rate);

            if (!_state.TryGetValue(charaId, out var st))
            {
                st = new CharaState();
                _state[charaId] = st;
            }

            // ── Validate / refresh SMR ────────────────────────────────────
            bool needRescan = st.SMR == null || st.SMR.sharedMesh == null;
            if (!needRescan)
            {
                try { if (!st.SMR.gameObject.activeInHierarchy && human.hiPoly) needRescan = true; }
                catch { }
            }
            if (needRescan)
            {
                // Invalidate records so BellyMask is recomputed for new SMR
                if (_records.TryGetValue(charaId, out var oldR))
                { foreach (var r in oldR) UndoRecord(r); _records.Remove(charaId); }

                st.SMR             = FindBodySMR(human);
                st.BonesFound      = false;
                st.FrameValid      = false;
                st.LastAppliedRate = float.NaN;
                if (st.SMR == null)
                {
                    Log.LogWarning($"[VtxMorph] id={charaId}: body SMR not found");
                    return;
                }
                Log.LogInfo($"[VtxMorph] id={charaId}: SMR \"{st.SMR.sharedMesh.name}\" " +
                            $"{st.SMR.sharedMesh.vertexCount}v readable={st.SMR.sharedMesh.isReadable}");
            }

            // ── Cheap re-apply (rate unchanged) ───────────────────────────
            if (Mathf.Approximately(rate, st.LastAppliedRate))
            {
                if (_records.TryGetValue(charaId, out var existR))
                    foreach (var rec in existR)
                        try { if (rec?.LastNewV != null && rec.Mesh != null) rec.Mesh.vertices = rec.LastNewV; }
                        catch { }
                return;
            }

            // ── Locate bones ──────────────────────────────────────────────
            if (!st.BonesFound)
            {
                if (!TryFindBones(human, st.SMR, st)) return;
                st.BonesFound = true;
                st.FrameValid = false;
            }

            // ── Build frame ───────────────────────────────────────────────
            if (!BuildFrame(st, st.SMR, out st.Frame)) return;
            st.FrameValid = true;

            // Only log when rate actually changes — prevents flood when UI calls InvalidateAll()
            if (!Mathf.Approximately(rate, st.LastLoggedRate))
            {
                Log.LogInfo($"[VtxMorph] id={charaId}: rate={rate:F3} " +
                            $"boneLen={st.Frame.BoneLen:F4} bindpose={st.Frame.BindPoseBased}");
                st.LastLoggedRate = rate;
            }

            if (!_records.ContainsKey(charaId)) _records[charaId] = new List<MeshRecord>();

            ApplySMR(_records[charaId], st.SMR, st.Frame, rate,
                     st.BellyBoneIdxSet, st.LegBoneIdxSet);
            st.LastAppliedRate = rate;
        }

        public static void Reset(int charaId)
        {
            if (_records.TryGetValue(charaId, out var recs))
                foreach (var rec in recs) UndoRecord(rec);
            if (_state.TryGetValue(charaId, out var st))
                st.LastAppliedRate = float.NaN;
        }

        public static void Forget(int charaId)
        {
            // Undo any applied deformation BEFORE discarding records.
            // Without this, the deformed mesh vertices persist and the next
            // Apply() call captures them as the "original" baseline — causing
            // the disc to accumulate across scene transitions.
            if (_records.TryGetValue(charaId, out var recs))
                foreach (var rec in recs) UndoRecord(rec);
            _state.Remove(charaId);
            _records.Remove(charaId);
        }

        public static void ForgetAll()
        {
            // Undo all applied deformations before clearing state.
            foreach (var recs in _records.Values)
                foreach (var rec in recs) UndoRecord(rec);
            _state.Clear();
            _records.Clear();
        }

        public static void InvalidateAll()
        {
            foreach (var st in _state.Values)
                st.LastAppliedRate = float.NaN;
        }

        public static string GetStatusLine(int charaId)
        {
            if (!_state.TryGetValue(charaId, out var st)) return "no state";
            string smr;
            try { smr = st.SMR?.sharedMesh != null
                    ? $"SMR={st.SMR.sharedMesh.name}({st.SMR.sharedMesh.vertexCount}v)"
                    : "SMR=missing"; } catch { smr = "SMR=err"; }
            string bone = st.BonesFound
                ? (st.FrameValid
                    ? $"bones=ok boneLen={st.Frame.BoneLen:F3} bindpose={st.Frame.BindPoseBased}"
                    : "bones=ok frame=invalid")
                : "bones=NOT FOUND";
            string rat = float.IsNaN(st.LastAppliedRate) ? "rate=pending" : $"rate={st.LastAppliedRate:F3}";
            return $"{smr}  {bone}  {rat}";
        }

        public static void DumpInfo(Human human, int charaId)
        {
            Log.LogInfo($"[VtxDump] ===== charaId={charaId} =====");
            if (human == null) { Log.LogInfo("[VtxDump] human is null"); return; }
            try
            {
                var all = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Log.LogInfo($"[VtxDump] SMRs: {all?.Count ?? 0}");
                if (all != null)
                    foreach (var s in all)
                    {
                        if (s == null) continue;
                        int bp = 0; try { bp = s.sharedMesh?.bindposes?.Length ?? 0; } catch { bp = -1; }
                        int bc = 0; try { bc = s.bones?.Length ?? 0; } catch { bc = -1; }
                        Log.LogInfo($"[VtxDump]  \"{s.name}\" verts={s.sharedMesh?.vertexCount ?? -1} " +
                                    $"bp={bp} bones={bc} active={s.gameObject.activeInHierarchy} face={IsFaceMesh(s)}");
                    }
            }
            catch (Exception e) { Log.LogInfo("[VtxDump] SMR scan: " + e.Message); }

            try
            {
                var allTf = human.gameObject.GetComponentsInChildren<Transform>(true);
                Log.LogInfo($"[VtxDump] Transforms: {allTf?.Count ?? 0}");
                Transform pelvis = null, spine = null;
                if (allTf != null)
                    foreach (var tf in allTf)
                    {
                        if (tf == null) continue;
                        if (pelvis == null && ArrHas(PelvisBones, tf.name)) pelvis = tf;
                        if (spine  == null && ArrHas(SpineBones,  tf.name)) spine  = tf;
                    }
                Log.LogInfo($"[VtxDump] pelvis={pelvis?.name ?? "NOT FOUND"} spine={spine?.name ?? "NOT FOUND"}");
            }
            catch (Exception e) { Log.LogInfo("[VtxDump] hierarchy: " + e.Message); }

            if (_state.TryGetValue(charaId, out var st))
            {
                Log.LogInfo($"[VtxDump] bones={st.BonesFound} frame={st.FrameValid} rate={st.LastAppliedRate:F3}");
                Log.LogInfo($"[VtxDump]  pelvisTf={st.PelvisTf?.name ?? "null"} spineTf={st.SpineTf?.name ?? "null"}");
                Log.LogInfo($"[VtxDump]  pelvisIdx={st.PelvisIdx} spineIdx={st.SpineIdx}");
                Log.LogInfo($"[VtxDump]  bellyBones={st.BellyBoneIdxSet?.Count ?? -1} legBones={st.LegBoneIdxSet?.Count ?? -1}");
                if (st.FrameValid)
                    Log.LogInfo($"[VtxDump]  center={st.Frame.Center} boneLen={st.Frame.BoneLen:F4}");
            }
            else Log.LogInfo("[VtxDump] no cached state");

            if (_records.TryGetValue(charaId, out var recs))
                foreach (var rec in recs)
                {
                    if (rec == null) continue;
                    int masked = 0;
                    if (rec.BellyMask != null) foreach (var m in rec.BellyMask) if (m) masked++;
                    Log.LogInfo($"[VtxDump]  MeshRecord mesh={rec.Mesh?.name} " +
                                $"origVerts={rec.OrigVerts?.Length ?? -1} bellyMask={masked}/{rec.BellyMask?.Length ?? 0}");
                }
            Log.LogInfo($"[VtxDump] ===== end =====");
        }

        // ── Body SMR discovery ────────────────────────────────────────────

        private static SkinnedMeshRenderer FindBodySMR(Human human)
        {
            SkinnedMeshRenderer best = null; int bestV = 0;
            try
            {
                var all = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (all != null)
                {
                    foreach (var s in all) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body" && s.gameObject.activeInHierarchy)  { Log.LogInfo($"[VtxMorph] FindBodySMR: active o_body"); return s; } }
                    foreach (var s in all) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body") { Log.LogInfo($"[VtxMorph] FindBodySMR: inactive o_body"); return s; } }
                    foreach (var s in all) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable || IsFaceMesh(s)) continue; int v = s.sharedMesh.vertexCount; if (v > bestV) { best = s; bestV = v; } }
                }
                Transform br = human?.body?.trfBodyBone;
                if (br != null && best == null)
                {
                    Transform sf = br; for (int i = 0; i < 4 && sf.parent != null; i++) sf = sf.parent;
                    if (sf.gameObject != human.gameObject)
                    {
                        var ba = sf.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        if (ba != null)
                        {
                            foreach (var s in ba) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body" && s.gameObject.activeInHierarchy) return s; }
                            foreach (var s in ba) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body") return s; }
                            foreach (var s in ba) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable || IsFaceMesh(s)) continue; int v = s.sharedMesh.vertexCount; if (v > bestV) { best = s; bestV = v; } }
                        }
                    }
                }
            }
            catch (Exception e) { Log.LogWarning("[VtxMorph] FindBodySMR: " + e.Message); }
            if (best != null) Log.LogInfo($"[VtxMorph] FindBodySMR: fallback \"{best.name}\" ({bestV}v)");
            return best;
        }

        private static bool IsFaceMesh(SkinnedMeshRenderer smr)
        {
            string id = ((smr.name ?? "") + "/" + (smr.gameObject?.name ?? "")).ToLowerInvariant();
            foreach (var kw in FaceKeywords) if (id.Contains(kw)) return true;
            return false;
        }

        // ── Bone discovery ────────────────────────────────────────────────

        private static bool TryFindBones(Human human, SkinnedMeshRenderer smr, CharaState st)
        {
            st.PelvisTf = st.SpineTf = st.LThighTf = st.RThighTf = null;
            st.PelvisIdx = st.SpineIdx = st.LThighIdx = st.RThighIdx = -1;
            st.BellyBoneIdxSet = null;
            st.LegBoneIdxSet   = null;

            // Step 1: Human hierarchy (reliable)
            try
            {
                var allTf = human.gameObject.GetComponentsInChildren<Transform>(true);
                Log.LogInfo($"[VtxMorph] TryFindBones: scanning {allTf?.Count ?? 0} transforms");
                if (allTf != null)
                    foreach (var tf in allTf)
                    {
                        if (tf == null) continue;
                        string n = tf.name;
                        if (st.PelvisTf == null && ArrHas(PelvisBones, n))  st.PelvisTf = tf;
                        if (st.SpineTf  == null && ArrHas(SpineBones,  n))  st.SpineTf  = tf;
                        if (st.LThighTf == null && ArrHas(LThighBones, n))  st.LThighTf = tf;
                        if (st.RThighTf == null && ArrHas(RThighBones, n))  st.RThighTf = tf;
                    }
            }
            catch (Exception e) { Log.LogWarning("[VtxMorph] TryFindBones hierarchy: " + e.Message); }

            if (st.PelvisTf == null || st.SpineTf == null)
            {
                Log.LogWarning($"[VtxMorph] TryFindBones: pelvis={st.PelvisTf?.name ?? "null"} spine={st.SpineTf?.name ?? "null"}");
                return false;
            }
            Log.LogInfo($"[VtxMorph] TryFindBones: pelvis={st.PelvisTf.name} spine={st.SpineTf.name}");

            // Step 2: Map to smr.bones[] + populate weight-filter sets
            try
            {
                var smrBones = smr.bones;
                int bc = smrBones?.Length ?? 0;

                if (bc > 0)
                {
                    var sb2 = new System.Text.StringBuilder("[VtxMorph] smr.bones sample: ");
                    for (int i = 0; i < Math.Min(12, bc); i++)
                    { sb2.Append(smrBones[i]?.name ?? "null"); if (i < bc-1 && i < 11) sb2.Append(", "); }
                    if (bc > 12) sb2.Append($"...({bc})");
                    Log.LogInfo(sb2.ToString());

                    IntPtr pelvisPtr = st.PelvisTf.Pointer;
                    IntPtr spinePtr  = st.SpineTf.Pointer;
                    IntPtr lPtr      = st.LThighTf?.Pointer ?? IntPtr.Zero;
                    IntPtr rPtr      = st.RThighTf?.Pointer ?? IntPtr.Zero;

                    st.BellyBoneIdxSet = new HashSet<int>();
                    st.LegBoneIdxSet   = new HashSet<int>();

                    for (int i = 0; i < bc; i++)
                    {
                        var b = smrBones[i];
                        if (b == null) continue;
                        string bn   = b.name;
                        IntPtr bptr = b.Pointer;

                        // Primary bone index mapping (name + pointer)
                        if (st.PelvisIdx < 0 && (ArrHas(PelvisBones, bn) || bptr == pelvisPtr)) st.PelvisIdx = i;
                        if (st.SpineIdx  < 0 && (ArrHas(SpineBones,  bn) || bptr == spinePtr))  st.SpineIdx  = i;
                        if (st.LThighIdx < 0 && (ArrHas(LThighBones, bn) || (lPtr != IntPtr.Zero && bptr == lPtr))) st.LThighIdx = i;
                        if (st.RThighIdx < 0 && (ArrHas(RThighBones, bn) || (rPtr != IntPtr.Zero && bptr == rPtr))) st.RThighIdx = i;

                        // Weight-filter bone sets (PP-style vertex selection)
                        // Belly: waist, spine, hip region
                        if (bn.Contains("waist") || bn.Contains("spine") ||
                            bn.Contains("hip")   || bn.Contains("belly"))
                            st.BellyBoneIdxSet.Add(i);
                        // Leg: thigh, leg (non-spine), knee
                        if (bn.Contains("thigh") ||
                            (bn.Contains("leg") && !bn.Contains("spine")) ||
                            bn.Contains("knee"))
                            st.LegBoneIdxSet.Add(i);
                    }

                    Log.LogInfo($"[VtxMorph] TryFindBones smr.bones={bc}: " +
                                $"pelvisIdx={st.PelvisIdx} spineIdx={st.SpineIdx} " +
                                $"bellySet={st.BellyBoneIdxSet.Count} legSet={st.LegBoneIdxSet.Count}");
                }
                else
                {
                    Log.LogWarning("[VtxMorph] TryFindBones: smr.bones empty — no bindpose / weight filter");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] TryFindBones smr.bones: " + e.Message);
            }

            return true;
        }

        // ── Frame construction ────────────────────────────────────────────
        //
        // Preferred: bindposes (pose-independent).
        // Fallback:  live bone positions via InverseTransformPoint (pose-dependent).

        private static bool BuildFrame(CharaState st, SkinnedMeshRenderer smr, out LocalFrame frame)
        {
            frame = default;
            try
            {
                Vector3 pelvisL, spineL;
                bool bindPoseUsed = false;

                if (st.PelvisIdx >= 0 && st.SpineIdx >= 0)
                {
                    try
                    {
                        var bp = smr.sharedMesh.bindposes;
                        int bpLen = bp?.Length ?? 0;
                        if (bpLen > st.PelvisIdx && bpLen > st.SpineIdx)
                        {
                            pelvisL      = bp[st.PelvisIdx].inverse.MultiplyPoint3x4(Vector3.zero);
                            spineL       = bp[st.SpineIdx].inverse.MultiplyPoint3x4(Vector3.zero);
                            bindPoseUsed = true;
                        }
                        else goto LiveFallback;
                    }
                    catch { goto LiveFallback; }
                    goto AfterPelvisSpine;
                }

                LiveFallback:
                {
                    if (st.PelvisTf == null || st.SpineTf == null) return false;
                    Transform root = null;
                    try { root = smr.rootBone?.parent ?? smr.rootBone ?? smr.transform; }
                    catch { root = smr.transform; }
                    pelvisL = root.InverseTransformPoint(st.PelvisTf.position);
                    spineL  = root.InverseTransformPoint(st.SpineTf.position);
                    Log.LogWarning("[VtxMorph] BuildFrame: LIVE FALLBACK (pose-dependent)");
                }

                AfterPelvisSpine:
                Vector3 upRaw = spineL - pelvisL;
                if (upRaw.sqrMagnitude < 1e-8f) return false;
                float   boneLen = upRaw.magnitude;
                Vector3 up      = upRaw / boneLen;

                // Lateral axis from thigh bones
                Vector3 right = Vector3.right;
                try
                {
                    Vector3 lL = Vector3.zero, rL = Vector3.zero;
                    bool gotLat = false;
                    if (bindPoseUsed && st.LThighIdx >= 0 && st.RThighIdx >= 0)
                    {
                        var bp = smr.sharedMesh.bindposes;
                        if (bp.Length > st.LThighIdx && bp.Length > st.RThighIdx)
                        { lL = bp[st.LThighIdx].inverse.MultiplyPoint3x4(Vector3.zero); rL = bp[st.RThighIdx].inverse.MultiplyPoint3x4(Vector3.zero); gotLat = true; }
                    }
                    if (!gotLat && st.LThighTf != null && st.RThighTf != null)
                    {
                        Transform root2 = null;
                        try { root2 = smr.rootBone?.parent ?? smr.rootBone ?? smr.transform; } catch { root2 = smr.transform; }
                        lL = root2.InverseTransformPoint(st.LThighTf.position);
                        rL = root2.InverseTransformPoint(st.RThighTf.position);
                        gotLat = true;
                    }
                    if (gotLat)
                    {
                        Vector3 tv = rL - lL; tv -= up * Vector3.Dot(tv, up);
                        if (tv.sqrMagnitude > 1e-8f) right = tv.normalized;
                    }
                }
                catch { }

                Vector3 fwd = Vector3.Cross(up, right).normalized;
                if (Vector3.Dot(fwd, Vector3.forward) < 0f) { fwd = -fwd; right = Vector3.Cross(up, fwd).normalized; }

                var p = BellyDeformSettings.Vtx;
                Vector3 center = Vector3.Lerp(pelvisL, spineL, Mathf.Clamp01(p.SpineLerpT))
                               + up  * (p.MoveY * boneLen)
                               + fwd * (p.MoveZ * boneLen);

                frame = new LocalFrame { Center = center, Up = up, Fwd = fwd, Right = right, BoneLen = boneLen, BindPoseBased = bindPoseUsed };
                Log.LogInfo($"[VtxMorph] BuildFrame: center={center} boneLen={boneLen:F4} bindpose={bindPoseUsed}");
                return true;
            }
            catch (Exception e) { Log.LogWarning("[VtxMorph] BuildFrame: " + e.Message); return false; }
        }

        // ── Per-SMR deformation ───────────────────────────────────────────

        private static void ApplySMR(
            List<MeshRecord> recs,
            SkinnedMeshRenderer smr,
            LocalFrame fr,
            float rate,
            HashSet<int> bellyBoneSet,
            HashSet<int> legBoneSet)
        {
            try
            {
                Mesh mesh = smr.sharedMesh;

                MeshRecord rec = null;
                foreach (var r in recs) if (r != null && r.Mesh == mesh) { rec = r; break; }

                Vector3[] cur = mesh.vertices;
                if (cur == null || cur.Length == 0) return;

                bool wasApplied = rec != null && rec.AppliedSig != 0
                    && cur.Length == rec.OrigVerts?.Length && Sig(cur) == rec.AppliedSig;

                if (rec == null)
                {
                    rec = new MeshRecord { Mesh = mesh, OrigVerts = (Vector3[])cur.Clone() };
                    // ── Compute belly mask (PP-style bone weight filter) ───────
                    rec.BellyMask     = ComputeBellyMask(mesh, cur.Length, bellyBoneSet, legBoneSet);
                    rec.BreastWeights = ComputeBreastWeights(mesh, cur.Length, smr);
                    recs.Add(rec);
                }
                else if (!wasApplied && rec.OrigVerts != null && cur.Length == rec.OrigVerts.Length)
                {
                    rec.OrigVerts = (Vector3[])cur.Clone();
                }
                else if (rec.OrigVerts == null)
                {
                    rec.OrigVerts = (Vector3[])cur.Clone();
                }

                var p       = BellyDeformSettings.Vtx;
                float boneLen = fr.BoneLen;
                float scale   = Mathf.Max(0.01f, p.InflationSize);
                float rS = Mathf.Max(p.RadiusSide,  1e-4f) * scale * boneLen;
                float rF = Mathf.Max(p.RadiusFront, 1e-4f) * scale * boneLen;
                float rB = Mathf.Max(p.RadiusBack,  1e-4f) * scale * boneLen;
                float rU = Mathf.Max(p.RadiusUp,    1e-4f) * scale * boneLen;
                float rD = Mathf.Max(p.RadiusDown,  1e-4f) * scale * boneLen;

                // ── RoundToSides parameters (PP source formula) ───────────
                // Smooth distance = rB + (rF-rB)/3 when rF > rB, else rB.
                float rtsSmoothDist = Mathf.Max(rB + (rF > rB ? (rF - rB) / 3f : 0f), 1e-4f);

                // ── Rate-scale parameters not already gated by str ────────
                // str = rate × edge-falloff already scales the sphere-projection,
                // ShiftY/Z, Drop, and FatFold.  Roundness, Stretch, and Taper
                // are transforms applied AFTER the projection and would otherwise
                // be at full value on day startDay+1.  Multiply by rate so that
                // all deformation effects grow in proportion to pregnancy progress.
                float effRoundness = p.Roundness * rate;
                float effStretchX  = p.StretchX  * rate;
                float effStretchY  = p.StretchY  * rate;
                float effStretchZ  = p.StretchZ  * rate;
                float effTaperY    = p.TaperY    * rate;
                float effTaperZ    = p.TaperZ    * rate;

                int n    = rec.OrigVerts.Length;
                var newV = new Vector3[n];
                int deformed = 0;
                bool[] mask = rec.BellyMask;

                for (int i = 0; i < n; i++)
                {
                    Vector3 lv = rec.OrigVerts[i];
                    newV[i] = lv;

                    // ── Bone-weight filter (PP mechanism #2 / #3) ─────────
                    // BellyMask = null means no filtering (bones not found or too few passed)
                    if (mask != null && !mask[i]) continue;

                    Vector3 d   = lv - fr.Center;
                    float upD   = Vector3.Dot(d, fr.Up);
                    float sdD   = Vector3.Dot(d, fr.Right);
                    float fwD   = Vector3.Dot(d, fr.Fwd);
                    float fwR   = fwD >= 0f ? rF : rB;
                    float upR   = upD >= 0f ? rD : rU;   // rD=upper-belly half, rU=lower-belly half (spine is below pelvis in bindpose)

                    float e = (sdD / rS) * (sdD / rS)
                            + (fwD / fwR) * (fwD / fwR)
                            + (upD / upR) * (upD / upR);
                    if (e >= 1f) continue;

                    float sqrtE = Mathf.Sqrt(e);

                    // ── EdgeSmooth (inner boundary falloff) ───────────────
                    float coreR = 1f - Mathf.Clamp01(p.EdgeSmooth) * 0.5f;
                    float tEdge = Mathf.Clamp01((sqrtE - coreR) / Mathf.Max(1f - coreR, 1e-4f));
                    float str   = rate * (1f - tEdge * tEdge * (3f - 2f * tEdge));

                    // ── RoundToSides (PP mechanism #4) ────────────────────
                    // forwardFromBack: 0 at belly back edge, (rF+rB) at front.
                    // Soft-ramp from 0 → 1 over [0, rtsSmoothDist].
                    float forwardFromBack = fwD + rB;
                    if (forwardFromBack <= 0f) continue; // behind belly, skip
                    float rtsT = forwardFromBack >= rtsSmoothDist ? 1f
                               : Mathf.Clamp01(forwardFromBack / rtsSmoothDist);
                    float rts  = rtsT * rtsT * (3f - 2f * rtsT); // SmoothStep ≈ PP's AnimationCurve
                    str *= rts;

                    if (str <= 1e-5f) continue;

                    // ── Sphere projection (PP core formula) ───────────────
                    float dm   = d.magnitude;
                    if (dm < 1e-6f) { d = fr.Fwd; dm = 1f; }
                    float sphereR = dm / Mathf.Max(sqrtE, 1e-4f);
                    Vector3 nlv   = Vector3.Lerp(lv, fr.Center + d.normalized * sphereR, str);

                    // ── Roundness ─────────────────────────────────────────
                    if (effRoundness != 0f)
                    {
                        float avgR = (rS + (rF + rB) * 0.5f + (rU + rD) * 0.5f) / 3f;
                        Vector3 toC = nlv - fr.Center; float magC = toC.magnitude;
                        if (magC > 1e-6f)
                            nlv = Vector3.Lerp(nlv, fr.Center + toC * (avgR / magC),
                                      Mathf.Clamp(effRoundness, -1f, 1f));
                    }

                    // ── Stretch ───────────────────────────────────────────
                    float sx = Mathf.Max(0.01f, 1f + effStretchX);
                    float sy = Mathf.Max(0.01f, 1f + effStretchY);
                    float sz = Mathf.Max(0.01f, 1f + effStretchZ);
                    if (sx != 1f || sy != 1f || sz != 1f)
                    {
                        Vector3 rel = nlv - fr.Center;
                        nlv = fr.Center
                            + fr.Right * (Vector3.Dot(rel, fr.Right) * sx)
                            + fr.Up    * (Vector3.Dot(rel, fr.Up)    * sy)
                            + fr.Fwd   * (Vector3.Dot(rel, fr.Fwd)   * sz);
                    }

                    // ── TaperY (×0.5 scale — PP tuning) ──────────────────
                    if (effTaperY != 0f)
                    {
                        Vector3 rel = nlv - fr.Center;
                        float upC   = Vector3.Dot(rel, fr.Up);
                        float hNorm = Mathf.Clamp(upC >= 0f ? upC / Mathf.Max(rU, 1e-4f) : upC / Mathf.Max(rD, 1e-4f), -1f, 1f);
                        float fac   = Mathf.Clamp(1f - hNorm * effTaperY * 0.5f, 0.1f, 2f);
                        nlv = fr.Center + fr.Up * upC
                            + fr.Right * (Vector3.Dot(rel, fr.Right) * fac)
                            + fr.Fwd   * (Vector3.Dot(rel, fr.Fwd)   * fac);
                    }

                    // ── TaperZ (×0.5 scale) ───────────────────────────────
                    if (effTaperZ != 0f)
                    {
                        Vector3 rel  = nlv - fr.Center;
                        float fwdC   = Vector3.Dot(rel, fr.Fwd);
                        float dNorm  = Mathf.Clamp(fwdC >= 0f ? fwdC / Mathf.Max(rF, 1e-4f) : fwdC / Mathf.Max(rB, 1e-4f), -1f, 1f);
                        float fac    = Mathf.Clamp(1f - dNorm * effTaperZ * 0.5f, 0.1f, 2f);
                        nlv = fr.Center + fr.Fwd * fwdC
                            + fr.Up    * (Vector3.Dot(rel, fr.Up)    * fac)
                            + fr.Right * (Vector3.Dot(rel, fr.Right) * fac);
                    }

                    // ── Shift ─────────────────────────────────────────────
                    if (p.ShiftY != 0f) nlv += fr.Up  * (p.ShiftY * boneLen * str);
                    if (p.ShiftZ != 0f) nlv += fr.Fwd * (p.ShiftZ * boneLen * str);

                    // ── Drop ──────────────────────────────────────────────
                    if (p.Drop != 0f)
                    {
                        float ff = Mathf.Clamp01(fwD / Mathf.Max(rF * 1.5f, 1e-4f));
                        nlv -= fr.Up * (rF * p.Drop * ff * str);
                    }

                    // ── ReduceRibStretchingZ (PP mechanism #5) ────────────
                    // Upper 70% of belly: gradually reduce forward push to avoid chest clip.
                    if (upD > rU * 0.3f)
                    {
                        float ribFrac = Mathf.Clamp01((upD - rU * 0.3f) / (rU * 0.7f));
                        float fwdDisp = Vector3.Dot(nlv - lv, fr.Fwd);
                        if (fwdDisp > 0f)
                            nlv -= fr.Fwd * (fwdDisp * ribFrac * 0.4f);
                    }

                    // ── FatFold ───────────────────────────────────────────
                    if (p.FatFold > 0f && fwD > 0f)
                    {
                        float fc   = -p.FatFoldHeight * rD;
                        float fw2  = Mathf.Max(0.005f, p.FatFoldGap * rD);
                        float dist = upD - fc;
                        float gaus = Mathf.Exp(-(dist * dist) / (2f * fw2 * fw2));
                        float ffrac = Mathf.Clamp01(fwD / rF);
                        nlv -= fr.Fwd * (p.FatFold * rF * gaus * ffrac * str);
                    }

                    // ── Back-face limiter ─────────────────────────────────
                    if (p.BackLimit > 0f && p.BackStrength > 0f && fwD < 0f)
                    {
                        float planeD = -(p.BackLimit * rB);
                        if (fwD < planeD)
                        {
                            float rangeD = Mathf.Max(p.BackSmooth * rB, 1e-5f);
                            float beyond = planeD - fwD;
                            float t      = Mathf.Clamp01(beyond / rangeD);
                            float sm     = t * t * (3f - 2f * t);
                            nlv = Vector3.Lerp(nlv, lv, sm * p.BackStrength);
                        }
                    }

                    // ── Breast guard (COM3D2-style bone-weight restore) ────
                    // Vertices that are skinned to breast bones are lerped back toward
                    // their original positions so that increasing RadiusUp does not
                    // pull the breasts forward.  The raw per-vertex breast-bone weight
                    // is stored in rec.BreastWeights; the user-facing multiplier
                    // (BreastGuardStrength) is applied here at runtime so that changing
                    // the slider takes effect without a full record invalidation.
                    if (rec.BreastWeights != null && rec.BreastWeights[i] > 0f)
                    {
                        float bgStr = Mathf.Max(0f, p.BreastGuardStrength);
                        if (bgStr > 0f)
                        {
                            float restore = Mathf.Clamp01(rec.BreastWeights[i] * 4f * bgStr);
                            if (restore >= 1f) { newV[i] = lv; continue; }   // fully blocked — no deformation
                            nlv = Vector3.Lerp(nlv, lv, restore);
                        }
                    }

                    newV[i] = nlv;
                    deformed++;
                }

                // Log only on first deformation or deformed-count changes (not every UI tick)
                if (rec.AppliedSig == 0 || deformed != rec.LastDeformedCount)
                    Log.LogInfo($"[VtxMorph] ApplySMR: {deformed}/{n} verts deformed rate={rate:F3}");

                rec.LastDeformedCount = deformed;

                if (deformed == 0)
                {
                    // If the mesh is currently in a deformed state (AppliedSig != 0), we must
                    // restore it.  Without this two things go wrong:
                    //   (a) The old deformed vertices stay on screen — the "residue" the user sees.
                    //   (b) The cheap re-apply path re-writes LastNewV (the old big deformation)
                    //       every frame, making the residue permanent.
                    // Only write when AppliedSig != 0; if the mesh is already clean we skip the
                    // write (and the RecalculateNormals call) to avoid the shading snap.
                    if (rec.AppliedSig != 0 && rec.OrigVerts != null)
                    {
                        try
                        {
                            mesh.vertices = rec.OrigVerts;
                            try { mesh.RecalculateNormals(); }  catch { }
                            try { mesh.RecalculateTangents(); } catch { }
                            mesh.RecalculateBounds();
                        }
                        catch { }
                    }
                    rec.AppliedSig = 0;
                    rec.LastNewV   = null;   // prevent cheap re-apply from resurrecting old deformation
                    return;
                }

                rec.AppliedSig = Sig(newV);
                rec.LastNewV   = newV;
                mesh.vertices  = newV;

                // ── PP mechanism #6: RecalculateNormals + Tangents ────────
                try { mesh.RecalculateNormals(); }   catch { }
                try { mesh.RecalculateTangents(); }  catch { }
                mesh.RecalculateBounds();
            }
            catch (Exception e) { Log.LogWarning("[VtxMorph] ApplySMR: " + e.Message); }
        }

        // ── Belly mask from bone weights (PP vertex-selection mechanism) ──

        private static bool[] ComputeBellyMask(
            Mesh mesh, int vertexCount,
            HashSet<int> bellyBoneSet, HashSet<int> legBoneSet)
        {
            if (bellyBoneSet == null || bellyBoneSet.Count == 0) return null;
            try
            {
                var bw = mesh.boneWeights;
                if (bw == null || bw.Length != vertexCount)
                {
                    Log.LogWarning($"[VtxMorph] BellyMask: boneWeights length mismatch " +
                                   $"({bw?.Length ?? -1} vs {vertexCount}) — skipping filter");
                    return null;
                }

                var mask = new bool[vertexCount];
                int pass = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    var w = bw[i];
                    float bellyW = 0f, legW = 0f;
                    if (bellyBoneSet.Contains(w.boneIndex0)) bellyW += w.weight0;
                    if (bellyBoneSet.Contains(w.boneIndex1)) bellyW += w.weight1;
                    if (bellyBoneSet.Contains(w.boneIndex2)) bellyW += w.weight2;
                    if (bellyBoneSet.Contains(w.boneIndex3)) bellyW += w.weight3;

                    if (bellyW < 0.02f) continue;  // PP threshold

                    if (legBoneSet != null)
                    {
                        if (legBoneSet.Contains(w.boneIndex0)) legW += w.weight0;
                        if (legBoneSet.Contains(w.boneIndex1)) legW += w.weight1;
                        if (legBoneSet.Contains(w.boneIndex2)) legW += w.weight2;
                        if (legBoneSet.Contains(w.boneIndex3)) legW += w.weight3;
                        if (legW > bellyW) continue;  // LowerBodyRestoreMask
                    }

                    mask[i] = true;
                    pass++;
                }

                Log.LogInfo($"[VtxMorph] BellyMask: {pass}/{vertexCount} vertices pass bone-weight filter");

                // Safety: if < 5% pass, the bone naming probably doesn't match → disable filter
                if (pass < vertexCount * 0.05f)
                {
                    Log.LogWarning($"[VtxMorph] BellyMask: too few vertices ({pass}) — disabling filter");
                    return null;
                }
                return mask;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] BellyMask: " + e.Message + " — no weight filtering");
                return null;
            }
        }

        // ── Breast-bone weight map ────────────────────────────────────────
        // For each vertex, stores the total skinning weight assigned to breast bones
        // (names containing "mune", "breast", "bust", "chichi", or "nipple").
        // This mirrors COM3D2's BreastBoneWeight / IsBreastBoneName pattern.
        // The raw weight is stored so that the user-facing BreastGuardStrength slider
        // can take effect immediately without re-computing the records.

        private static float[] ComputeBreastWeights(Mesh mesh, int vertexCount, SkinnedMeshRenderer smr)
        {
            try
            {
                var bw = mesh.boneWeights;
                if (bw == null || bw.Length != vertexCount) return null;

                var smrBones = smr.bones;
                int bc = smrBones?.Length ?? 0;
                if (bc == 0) return null;

                // Build breast-bone index set
                var breastSet = new HashSet<int>();
                for (int b = 0; b < bc; b++)
                {
                    var bone = smrBones[b];
                    if (bone != null && IsBreastBoneName(bone.name))
                        breastSet.Add(b);
                }
                if (breastSet.Count == 0)
                {
                    Log.LogInfo("[VtxMorph] BreastWeights: no breast bones found — guard disabled");
                    return null;
                }
                Log.LogInfo($"[VtxMorph] BreastWeights: found {breastSet.Count} breast bone(s)");

                var weights = new float[vertexCount];
                int found = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    var w = bw[i];
                    float total = 0f;
                    if (breastSet.Contains(w.boneIndex0)) total += w.weight0;
                    if (breastSet.Contains(w.boneIndex1)) total += w.weight1;
                    if (breastSet.Contains(w.boneIndex2)) total += w.weight2;
                    if (breastSet.Contains(w.boneIndex3)) total += w.weight3;
                    if (total > 0f) found++;
                    weights[i] = Mathf.Clamp01(total);
                }
                Log.LogInfo($"[VtxMorph] BreastWeights: {found}/{vertexCount} vertices have breast-bone weight");
                return found > 0 ? weights : null;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ComputeBreastWeights: " + e.Message + " — guard disabled");
                return null;
            }
        }

        private static bool IsBreastBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("mune")     // KKS/SVS: cf_j_mune00_L/R, cf_d_mune*, cf_s_mune*
                || lower.Contains("breast")
                || lower.Contains("bust")
                || lower.Contains("chichi")   // COM3D2 alias
                || lower.Contains("chikubi")  // nipple (COM3D2)
                || lower.Contains("nipple");
        }

        private static void UndoRecord(MeshRecord rec)
        {
            if (rec?.Mesh == null || rec.OrigVerts == null || rec.AppliedSig == 0) return;
            try
            {
                rec.Mesh.vertices = rec.OrigVerts;
                try { rec.Mesh.RecalculateNormals(); }  catch { }
                try { rec.Mesh.RecalculateTangents(); } catch { }
                rec.Mesh.RecalculateBounds();
                rec.AppliedSig = 0;
                rec.LastNewV   = null;
            }
            catch { }
        }

        // ── Utilities ─────────────────────────────────────────────────────

        private static bool ArrHas(string[] arr, string val)
        {
            foreach (var s in arr) if (s == val) return true;
            return false;
        }

        private static int Sig(Vector3[] v)
        {
            if (v == null || v.Length == 0) return 0;
            unchecked
            {
                int h = v.Length; int step = Math.Max(1, v.Length / 16);
                for (int i = 0; i < v.Length; i += step)
                { h = h * 397 ^ v[i].x.GetHashCode(); h = h * 397 ^ v[i].y.GetHashCode(); h = h * 397 ^ v[i].z.GetHashCode(); }
                return h == 0 ? 1 : h;
            }
        }
    }
}

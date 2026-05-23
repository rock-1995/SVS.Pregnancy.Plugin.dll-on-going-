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
            public IntPtr HumanPtr = IntPtr.Zero;
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
            public List<BodyLayerEntry> BodyLayerEntries;
            public List<ClothEntry> ClothEntries;
            public List<AccessoryEntry> AccessoryEntries;
            public Dictionary<Transform, TransformRecord> AccessoryTransformRecords = new();
            public int        LastBodyLayerApplied = -1;
            public int        LastClothApplied = -1;
            public int        LastAccessoryApplied = -1;
            public int        CharaId = -1;
            public bool       LastSkippedInactive = false;
            public int        LastMeshSpyFrame = -100000;
            public int        LastMeshSpySig = 0;
            public int        LastMeshSpySmrCount = -1;
            public int        LastMeshSpyMfCount = -1;
            public int        LastAccessoryTargetScanFrame = -100000;
            public int        LastAccessoryTargetSig = 0;
            public int        LastAccessoryTargetCount = -1;
        }

        private class ClothEntry
        {
            public SkinnedMeshRenderer SMR;
            public HashSet<int> BellyBoneIdxSet;
            public HashSet<int> LegBoneIdxSet;
            public ClothKind Kind;
        }

        private class BodyLayerEntry
        {
            public SkinnedMeshRenderer SMR;
            public HashSet<int> BellyBoneIdxSet;
            public HashSet<int> LegBoneIdxSet;
        }

        private enum ClothKind
        {
            Top,
            Bottom,
            Bra,
            Shorts,
            Panst,
            Other
        }

        private class AccessoryEntry
        {
            public Component Component;
            public Mesh Mesh;
            public Transform Transform;
            public Transform ApplyTransform;
            public AccessoryMode Mode;
            public string AnchorName;
            public bool IsSkinned;
            public bool RigidSampleValid;
            public Vector3 RigidSampleBody;
        }

        private enum AccessoryMode
        {
            WaistSurface,
            RigidMove
        }

        private class TransformRecord
        {
            public Transform Transform;
            public Transform Parent;
            public Vector3 OrigLocalPosition;
            public Vector3 LastAppliedLocalPosition;
            public Quaternion OrigLocalRotation;
            public Quaternion LastAppliedLocalRotation;
            public bool Applied;
        }

        // ── Mesh deformation record ───────────────────────────────────────
        private class MeshRecord
        {
            public Mesh      Mesh;
            public Vector3[] OrigVerts;        // bind-pose baseline
            public Vector3[] LastNewV;
            public bool[]    BellyMask;        // per-vertex bone-weight pass/fail (null = no filter)
            public float[]   BreastWeights;    // per-vertex breast-bone weight sum 0..1 (null = not computed / no breast bones found)
            public List<int>[] Neighbors;       // mesh topology cache for body-anchor basis building
            public int       AppliedSig;
            public int       LastDeformedCount = -1;  // vertex count from last ApplySMR — used to suppress repeated log lines
        }

        private class BodyAnchorContext
        {
            public readonly List<BodyAnchorMesh> Meshes = new();
            public readonly List<BodyAnchorPoint> Points = new();
            public readonly Dictionary<int, List<int>> Buckets = new();
            public readonly List<BodySurfaceTriangle> SurfaceTriangles = new();
            public readonly Dictionary<int, List<int>> SurfaceBuckets = new();
            public float CellSize;
            public float MinMovedSq;
            public int BasisDirect;
            public int BasisSecondOrder;
            public int BasisFailed;
            public int ClothQueries;
            public int ClothHits;
            public int ClothMisses;
            public int ClothRejected;
        }

        private class BodyAnchorMesh
        {
            public Vector3[] Original;
            public Vector3[] Morphed;
            public bool[] Valid;
            public bool[] Affected;
            public BodyAnchorBasis[] Bases;
            public List<int>[] SurfaceTrianglesByVertex;
        }

        private struct BodyAnchorPoint
        {
            public int MeshIndex;
            public int VertexIndex;
            public Vector3 Original;
            public float OriginalUp;
        }

        private struct BodyAnchorBasis
        {
            public bool Valid;
            public int Neighbor1;
            public int Neighbor2;
            public Matrix4x4 OriginalInverse;
        }

        private struct BodySurfaceTriangle
        {
            public int MeshIndex;
            public int A;
            public int B;
            public int C;
            public Vector3 Center;
            public float RadiusSq;
        }

        private struct BodySurfaceHit
        {
            public int TriangleIndex;
            public Vector3 Closest;
            public Vector3 Barycentric;
            public Vector3 Normal;
            public float DistanceSq;
        }

        private static readonly Dictionary<long, CharaState>       _state   = new();
        private static readonly Dictionary<long, List<MeshRecord>> _records = new();

        private struct LocalFrame
        {
            public Vector3 Center, Up, Fwd, Right;
            public float   BoneLen;
            public bool    BindPoseBased;
        }

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Vtx");

        private static readonly bool RuntimeInfoLogEnabled = false;

        private static void RuntimeLogInfo(string message)
        {
            if (RuntimeInfoLogEnabled)
                Log.LogInfo(message);
        }

        private const bool MeshChangeSpyEnabled = false;
        private const int MeshChangeSpyIntervalFrames = 45;
        private const int MeshChangeSpyMaxSMRLines = 96;
        private const int MeshChangeSpyMaxMeshFilterLines = 48;
        private const int AccessoryTargetScanIntervalFrames = 45;

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

            IntPtr humanPtr = IntPtr.Zero;
            try { humanPtr = human.Pointer; } catch { }
            long stateKey = StateKey(charaId, humanPtr);

            if (!_state.TryGetValue(stateKey, out var st))
            {
                st = new CharaState { CharaId = charaId, HumanPtr = humanPtr };
                _state[stateKey] = st;
            }
            st.CharaId = charaId;

            // ── Validate / refresh SMR ────────────────────────────────────
            bool needRescan = st.SMR == null || st.SMR.sharedMesh == null || st.HumanPtr != humanPtr;
            if (!needRescan)
            {
                try { if (!st.SMR.gameObject.activeInHierarchy && human.hiPoly) needRescan = true; }
                catch { }
            }
            if (needRescan)
            {
                // Invalidate records so BellyMask is recomputed for new SMR
                if (_records.TryGetValue(stateKey, out var oldR))
                { foreach (var r in oldR) UndoRecord(r); _records.Remove(stateKey); }
                UndoAccessoryTransforms(st);

                st.SMR             = FindBodySMR(human);
                st.BonesFound      = false;
                st.FrameValid      = false;
                st.LastAppliedRate = float.NaN;
                st.HumanPtr        = humanPtr;
                st.CharaId         = charaId;
                st.ClothEntries    = null;
                st.LastClothApplied = -1;
                st.BodyLayerEntries = null;
                st.LastBodyLayerApplied = -1;
                st.AccessoryEntries = null;
                st.LastAccessoryApplied = -1;
                st.LastSkippedInactive = false;
                if (st.SMR == null)
                {
                    Log.LogWarning($"[VtxMorph] id={charaId}: body SMR not found");
                    return;
                }
                RuntimeLogInfo($"[VtxMorph] id={charaId}: SMR \"{st.SMR.sharedMesh.name}\" " +
                               $"{st.SMR.sharedMesh.vertexCount}v readable={st.SMR.sharedMesh.isReadable}");
            }

            bool inactiveBody = false;
            try { inactiveBody = st.SMR != null && !st.SMR.gameObject.activeInHierarchy; } catch { }
            if (inactiveBody)
            {
                st.LastSkippedInactive = true;
                st.LastAppliedRate = rate;
                return;
            }
            if (st.LastSkippedInactive)
            {
                st.LastSkippedInactive = false;
                st.LastAppliedRate = float.NaN;
                UndoAccessoryTransforms(st);
                st.ClothEntries = null;
                st.LastClothApplied = -1;
                st.BodyLayerEntries = null;
                st.LastBodyLayerApplied = -1;
                st.AccessoryEntries = null;
                st.LastAccessoryApplied = -1;
            }

            if (MeshChangeSpyEnabled && SpyDetectHumanMeshChange(human, charaId, rate, st))
            {
                st.LastAppliedRate = float.NaN;
                st.ClothEntries = null;
                st.LastClothApplied = -1;
                st.BodyLayerEntries = null;
                st.LastBodyLayerApplied = -1;
                st.AccessoryEntries = null;
                st.LastAccessoryApplied = -1;
            }

            if (DetectAccessoryTargetChange(human, charaId, st))
            {
                UndoAccessoryTransforms(st);
                st.LastAppliedRate = float.NaN;
                st.AccessoryEntries = null;
                st.LastAccessoryApplied = -1;
            }

            // ── Cheap re-apply (rate unchanged) ───────────────────────────
            if (RateClose(rate, st.LastAppliedRate))
                return;

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
                RuntimeLogInfo($"[VtxMorph] id={charaId}: rate={rate:F3} " +
                               $"boneLen={st.Frame.BoneLen:F4} bindpose={st.Frame.BindPoseBased}");
                st.LastLoggedRate = rate;
            }

            if (!_records.ContainsKey(stateKey)) _records[stateKey] = new List<MeshRecord>();

            ApplySMR(_records[stateKey], st.SMR, st.Frame, rate,
                     st.BellyBoneIdxSet, st.LegBoneIdxSet, false, 1f, null);
            ApplyBodyLayerSMRs(human, charaId, _records[stateKey], st, rate);

            BodyAnchorContext bodyAnchor = CreateBodyAnchorContext(st.Frame);
            MeshRecord bodyRec = FindRecord(_records[stateKey], st.SMR.sharedMesh);
            if (bodyRec?.LastNewV != null)
            {
                AddBodyAnchorMesh(bodyAnchor, st.SMR, bodyRec, bodyRec.BellyMask, bodyRec.LastNewV, st.Frame);
                FinalizeBodyAnchorContext(bodyAnchor);
            }

            ApplyClothSMRs(human, charaId, _records[stateKey], st, rate, bodyAnchor);
            ApplyAccessories(human, charaId, _records[stateKey], st, rate, bodyAnchor);
            st.LastAppliedRate = rate;
        }

        public static void Reset(int charaId)
        {
            foreach (var key in KeysForChara(charaId))
            {
                if (_records.TryGetValue(key, out var recs))
                    foreach (var rec in recs) UndoRecord(rec);
                if (_state.TryGetValue(key, out var st))
                {
                    UndoAccessoryTransforms(st);
                    st.LastAppliedRate = float.NaN;
                    st.LastSkippedInactive = false;
                }
            }
        }

        public static void Forget(int charaId)
        {
            // Undo any applied deformation BEFORE discarding records.
            // Without this, the deformed mesh vertices persist and the next
            // Apply() call captures them as the "original" baseline — causing
            // the disc to accumulate across scene transitions.
            foreach (var key in KeysForChara(charaId))
            {
                if (_records.TryGetValue(key, out var recs))
                    foreach (var rec in recs) UndoRecord(rec);
                if (_state.TryGetValue(key, out var st))
                    UndoAccessoryTransforms(st);
                _state.Remove(key);
                _records.Remove(key);
            }
        }

        public static void ForgetAll()
        {
            // Undo all applied deformations before clearing state.
            foreach (var recs in _records.Values)
                foreach (var rec in recs) UndoRecord(rec);
            foreach (var st in _state.Values)
                UndoAccessoryTransforms(st);
            _state.Clear();
            _records.Clear();
        }

        public static void InvalidateAll()
        {
            foreach (var st in _state.Values)
            {
                st.LastAppliedRate = float.NaN;
                st.ClothEntries = null;
                st.LastClothApplied = -1;
                st.BodyLayerEntries = null;
                st.LastBodyLayerApplied = -1;
                st.AccessoryEntries = null;
                st.LastAccessoryApplied = -1;
            }
        }

        public static string GetStatusLine(int charaId)
        {
            CharaState st = null;
            foreach (var v in _state.Values)
            {
                if (v == null || v.CharaId != charaId) continue;
                if (st == null || (v.SMR != null && v.SMR.gameObject.activeInHierarchy))
                    st = v;
            }
            if (st == null) return "no state";
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

            long dumpKey = StateKey(charaId, human.Pointer);
            if (_state.TryGetValue(dumpKey, out var st))
            {
                Log.LogInfo($"[VtxDump] bones={st.BonesFound} frame={st.FrameValid} rate={st.LastAppliedRate:F3}");
                Log.LogInfo($"[VtxDump]  pelvisTf={st.PelvisTf?.name ?? "null"} spineTf={st.SpineTf?.name ?? "null"}");
                Log.LogInfo($"[VtxDump]  pelvisIdx={st.PelvisIdx} spineIdx={st.SpineIdx}");
                Log.LogInfo($"[VtxDump]  bellyBones={st.BellyBoneIdxSet?.Count ?? -1} legBones={st.LegBoneIdxSet?.Count ?? -1}");
                if (st.FrameValid)
                    Log.LogInfo($"[VtxDump]  center={st.Frame.Center} boneLen={st.Frame.BoneLen:F4}");
            }
            else Log.LogInfo("[VtxDump] no cached state");

            if (_records.TryGetValue(dumpKey, out var recs))
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

        private static bool SpyDetectHumanMeshChange(Human human, int charaId, float rate, CharaState st)
        {
            try
            {
                if (human == null || human.gameObject == null || st == null) return false;

                int frame = Time.frameCount;
                if (st.LastMeshSpyFrame >= 0 && frame - st.LastMeshSpyFrame < MeshChangeSpyIntervalFrames)
                    return false;
                st.LastMeshSpyFrame = frame;

                var smrs = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mfs = human.gameObject.GetComponentsInChildren<MeshFilter>(true);

                int smrCount = smrs?.Count ?? 0;
                int mfCount = mfs?.Count ?? 0;
                int sig = 17;

                if (smrs != null)
                    foreach (var smr in smrs)
                        sig = AppendSpySMRSignature(sig, smr);
                if (mfs != null)
                    foreach (var mf in mfs)
                        sig = AppendSpyMeshFilterSignature(sig, mf);

                bool first = st.LastMeshSpySig == 0 && st.LastMeshSpySmrCount < 0 && st.LastMeshSpyMfCount < 0;
                bool changed = first || st.LastMeshSpySig != sig || st.LastMeshSpySmrCount != smrCount || st.LastMeshSpyMfCount != mfCount;
                if (!changed) return false;

                RuntimeLogInfo($"[VtxMorphSpy] MeshChange id={charaId} first={first} rate={rate:F3} sig={sig} prevSig={st.LastMeshSpySig} smrs={smrCount} prevSmrs={st.LastMeshSpySmrCount} meshFilters={mfCount} prevMeshFilters={st.LastMeshSpyMfCount} root=\"{PathOf(human.transform)}\"");

                int shown = 0;
                if (smrs != null)
                {
                    int index = 0;
                    foreach (var smr in smrs)
                    {
                        if (smr == null) { index++; continue; }
                        LogSpySMR(charaId, index, smr, st.SMR);
                        shown++;
                        index++;
                        if (shown >= MeshChangeSpyMaxSMRLines)
                        {
                            RuntimeLogInfo($"[VtxMorphSpy] SMR truncated at {shown}/{smrCount}");
                            break;
                        }
                    }
                }

                int mfShown = 0;
                if (mfs != null)
                {
                    int index = 0;
                    foreach (var mf in mfs)
                    {
                        if (mf == null) { index++; continue; }
                        if (ShouldLogSpyMeshFilter(mf))
                        {
                            LogSpyMeshFilter(charaId, index, mf);
                            mfShown++;
                            if (mfShown >= MeshChangeSpyMaxMeshFilterLines)
                            {
                                RuntimeLogInfo($"[VtxMorphSpy] MeshFilter truncated at {mfShown}/{mfCount}");
                                break;
                            }
                        }
                        index++;
                    }
                }

                st.LastMeshSpySig = sig;
                st.LastMeshSpySmrCount = smrCount;
                st.LastMeshSpyMfCount = mfCount;
                return !first;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorphSpy] MeshChange failed: " + e.Message);
                return false;
            }
        }

        private static int AppendSpySMRSignature(int hash, SkinnedMeshRenderer smr)
        {
            unchecked
            {
                hash = SpyHash(hash, "SMR");
                if (smr == null) return SpyHash(hash, "null");
                Mesh mesh = null;
                try { mesh = smr.sharedMesh; } catch { }
                hash = SpyHash(hash, smr.name);
                hash = SpyHash(hash, PathOf(smr.transform));
                hash = SpyHash(hash, smr.enabled ? 1 : 0);
                try { hash = SpyHash(hash, smr.gameObject.activeSelf ? 1 : 0); } catch { }
                try { hash = SpyHash(hash, smr.gameObject.activeInHierarchy ? 1 : 0); } catch { }
                try { hash = SpyHash(hash, smr.rootBone?.name); } catch { }
                try { hash = SpyHash(hash, smr.bones?.Length ?? -1); } catch { }
                if (mesh != null)
                {
                    hash = SpyHash(hash, mesh.GetInstanceID());
                    hash = SpyHash(hash, mesh.name);
                    hash = SpyHash(hash, mesh.vertexCount);
                    try { hash = SpyHash(hash, mesh.isReadable ? 1 : 0); } catch { }
                }
                return hash;
            }
        }

        private static int AppendSpyMeshFilterSignature(int hash, MeshFilter mf)
        {
            unchecked
            {
                hash = SpyHash(hash, "MF");
                if (mf == null) return SpyHash(hash, "null");
                Mesh mesh = null;
                try { mesh = mf.sharedMesh; } catch { }
                hash = SpyHash(hash, mf.name);
                hash = SpyHash(hash, PathOf(mf.transform));
                try { hash = SpyHash(hash, mf.gameObject.activeSelf ? 1 : 0); } catch { }
                try { hash = SpyHash(hash, mf.gameObject.activeInHierarchy ? 1 : 0); } catch { }
                if (mesh != null)
                {
                    hash = SpyHash(hash, mesh.GetInstanceID());
                    hash = SpyHash(hash, mesh.name);
                    hash = SpyHash(hash, mesh.vertexCount);
                    try { hash = SpyHash(hash, mesh.isReadable ? 1 : 0); } catch { }
                }
                return hash;
            }
        }

        private static int SpyHash(int hash, int value)
        {
            unchecked { return hash * 397 ^ value; }
        }

        private static int SpyHash(int hash, string value)
        {
            unchecked { return hash * 397 ^ (value ?? "").GetHashCode(); }
        }

        private static bool DetectAccessoryTargetChange(Human human, int charaId, CharaState st)
        {
            try
            {
                if (human == null || human.gameObject == null || st == null) return false;

                int frame = Time.frameCount;
                if (st.LastAccessoryTargetScanFrame >= 0
                    && frame - st.LastAccessoryTargetScanFrame < AccessoryTargetScanIntervalFrames)
                    return false;
                st.LastAccessoryTargetScanFrame = frame;

                ComputeAccessoryTargetSignature(human, out int sig, out int count);
                bool first = st.LastAccessoryTargetCount < 0;
                bool changed = !first && (st.LastAccessoryTargetSig != sig || st.LastAccessoryTargetCount != count);

                if (first || changed)
                {
                    RuntimeLogInfo($"[VtxMorph] AccessoryTargetScan id={charaId}: first={first} changed={changed} count={count} prevCount={st.LastAccessoryTargetCount} sig={sig} prevSig={st.LastAccessoryTargetSig}");
                    st.LastAccessoryTargetSig = sig;
                    st.LastAccessoryTargetCount = count;
                }

                return changed;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] DetectAccessoryTargetChange: " + e.Message);
                return false;
            }
        }

        private static void ComputeAccessoryTargetSignature(Human human, out int sig, out int count)
        {
            sig = 17;
            count = 0;
            try
            {
                var mfs = human.gameObject.GetComponentsInChildren<MeshFilter>(true);
                if (mfs != null)
                {
                    foreach (var mf in mfs)
                    {
                        if (mf == null || mf.transform == null || mf.sharedMesh == null) continue;
                        if (!TryGetAccessoryMode(mf.transform, out AccessoryMode mode, out Transform anchor, out string anchorName)) continue;
                        Transform applyTf = FindAccessoryControlTransform(mf.transform, anchor);
                        sig = SpyHash(sig, "TargetMF");
                        sig = SpyHash(sig, anchorName);
                        sig = SpyHash(sig, mode.ToString());
                        sig = SpyHash(sig, PathOf(mf.transform));
                        sig = SpyHash(sig, PathOf(applyTf));
                        sig = SpyHash(sig, mf.sharedMesh.GetInstanceID());
                        sig = SpyHash(sig, mf.sharedMesh.vertexCount);
                        try { sig = SpyHash(sig, mf.gameObject.activeInHierarchy ? 1 : 0); } catch { }
                        count++;
                    }
                }

                var smrs = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs != null)
                {
                    foreach (var smr in smrs)
                    {
                        if (smr == null || smr.transform == null || smr.sharedMesh == null) continue;
                        if (!TryGetAccessoryMode(smr.transform, out AccessoryMode mode, out Transform anchor, out string anchorName)) continue;
                        Transform applyTf = FindAccessoryControlTransform(smr.transform, anchor);
                        sig = SpyHash(sig, "TargetSMR");
                        sig = SpyHash(sig, anchorName);
                        sig = SpyHash(sig, mode.ToString());
                        sig = SpyHash(sig, PathOf(smr.transform));
                        sig = SpyHash(sig, PathOf(applyTf));
                        sig = SpyHash(sig, smr.sharedMesh.GetInstanceID());
                        sig = SpyHash(sig, smr.sharedMesh.vertexCount);
                        try { sig = SpyHash(sig, smr.gameObject.activeInHierarchy ? 1 : 0); } catch { }
                        count++;
                    }
                }
            }
            catch { }
            if (sig == 0) sig = 1;
        }

        private static void LogSpySMR(int charaId, int index, SkinnedMeshRenderer smr, SkinnedMeshRenderer bodySMR)
        {
            try
            {
                Mesh mesh = null;
                try { mesh = smr.sharedMesh; } catch { }
                string path = PathOf(smr.transform);
                string id = ((smr.name ?? "") + "/" + (mesh?.name ?? "") + "/" + path).ToLowerInvariant();
                bool isBody = bodySMR != null && smr == bodySMR;
                bool face = IsFaceMesh(smr);
                bool clothName = LooksClothLikeId(id);
                bool accessoryName = LooksAccessoryLikeId(id);
                bool process = IsBodyMorphClothSMR(smr, bodySMR);
                ClothKind kind = process ? ClassifyClothSMR(smr) : ClothKind.Other;
                bool readable = false;
                int verts = -1;
                int bindposes = -1;
                int bones = -1;
                string root = "null";
                bool enabled = false;
                bool activeSelf = false;
                bool activeHierarchy = false;

                try { readable = mesh != null && mesh.isReadable; } catch { }
                try { verts = mesh?.vertexCount ?? -1; } catch { }
                try { bindposes = mesh?.bindposes?.Length ?? -1; } catch { }
                try { bones = smr.bones?.Length ?? -1; } catch { }
                try { root = smr.rootBone?.name ?? "null"; } catch { }
                try { enabled = smr.enabled; } catch { }
                try { activeSelf = smr.gameObject.activeSelf; } catch { }
                try { activeHierarchy = smr.gameObject.activeInHierarchy; } catch { }

                string relevantBones = SummarizeRelevantBones(smr, out int bellyBones, out int legBones, out int breastBones);
                bool bellyAttachment = bellyBones > 0 || IsBellyOrWaistBoneName(root) || PathHasBellyAnchor(path);
                bool accessoryBelly = accessoryName && bellyAttachment && !process;

                string decision;
                if (isBody) decision = "body";
                else if (process) decision = "cloth:" + kind;
                else if (accessoryBelly) decision = "accessoryBellyCandidate";
                else if (accessoryName) decision = "accessoryOther";
                else if (clothName) decision = "clothNameButRuleFailed";
                else if (face) decision = "skipFace";
                else if (!readable) decision = "skipNotReadable";
                else if (bellyAttachment) decision = "bellyBoneUnknown";
                else decision = "other";

                RuntimeLogInfo($"[VtxMorphSpy] SMR id={charaId} idx={index} decision={decision} process={process} name=\"{smr.name}\" mesh=\"{mesh?.name ?? "null"}\" verts={verts} readable={readable} bindposes={bindposes} bones={bones} bellyBones={bellyBones} legBones={legBones} breastBones={breastBones} root=\"{root}\" enabled={enabled} active={activeSelf}/{activeHierarchy} relBones=\"{relevantBones}\" path=\"{path}\"");
            }
            catch (Exception e)
            {
                RuntimeLogInfo($"[VtxMorphSpy] SMR id={charaId} idx={index} error={e.Message}");
            }
        }

        private static bool ShouldLogSpyMeshFilter(MeshFilter mf)
        {
            try
            {
                if (mf == null) return false;
                Mesh mesh = null;
                try { mesh = mf.sharedMesh; } catch { }
                string id = ((mf.name ?? "") + "/" + (mesh?.name ?? "") + "/" + PathOf(mf.transform)).ToLowerInvariant();
                return LooksAccessoryLikeId(id) || LooksClothLikeId(id) || PathHasBellyAnchor(id);
            }
            catch { return false; }
        }

        private static void LogSpyMeshFilter(int charaId, int index, MeshFilter mf)
        {
            try
            {
                Mesh mesh = null;
                try { mesh = mf.sharedMesh; } catch { }
                string path = PathOf(mf.transform);
                string id = ((mf.name ?? "") + "/" + (mesh?.name ?? "") + "/" + path).ToLowerInvariant();
                bool readable = false;
                int verts = -1;
                bool activeSelf = false;
                bool activeHierarchy = false;
                try { readable = mesh != null && mesh.isReadable; } catch { }
                try { verts = mesh?.vertexCount ?? -1; } catch { }
                try { activeSelf = mf.gameObject.activeSelf; } catch { }
                try { activeHierarchy = mf.gameObject.activeInHierarchy; } catch { }

                bool accessory = LooksAccessoryLikeId(id);
                bool belly = PathHasBellyAnchor(path);
                string decision = accessory && belly ? "accessoryTransformCandidate"
                    : accessory ? "accessoryMeshFilter"
                    : LooksClothLikeId(id) ? "clothMeshFilter"
                    : belly ? "bellyPathMeshFilter"
                    : "otherMeshFilter";

                RuntimeLogInfo($"[VtxMorphSpy] MeshFilter id={charaId} idx={index} decision={decision} name=\"{mf.name}\" mesh=\"{mesh?.name ?? "null"}\" verts={verts} readable={readable} active={activeSelf}/{activeHierarchy} parent=\"{mf.transform?.parent?.name ?? "null"}\" path=\"{path}\"");
            }
            catch (Exception e)
            {
                RuntimeLogInfo($"[VtxMorphSpy] MeshFilter id={charaId} idx={index} error={e.Message}");
            }
        }

        private static string SummarizeRelevantBones(SkinnedMeshRenderer smr, out int bellyBones, out int legBones, out int breastBones)
        {
            bellyBones = 0;
            legBones = 0;
            breastBones = 0;
            try
            {
                var bones = smr?.bones;
                int count = bones?.Length ?? 0;
                if (count == 0) return "";

                var shown = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string name = bones[i]?.name ?? "";
                    bool belly = IsBellyOrWaistBoneName(name);
                    bool leg = IsLegBoneName(name);
                    bool breast = IsBreastBoneName(name);
                    if (belly) bellyBones++;
                    if (leg) legBones++;
                    if (breast) breastBones++;
                    if ((belly || leg || breast) && shown.Count < 12)
                        shown.Add(i + ":" + name);
                }
                if (bellyBones + legBones + breastBones > shown.Count)
                    shown.Add("...");
                return string.Join(",", shown);
            }
            catch { return "bone-summary-error"; }
        }

        private static bool LooksClothLikeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.Contains("o_top") || id.Contains("/top/") || id.Contains("n_top") ||
                   id.Contains("o_bot") || id.Contains("/bot/") || id.Contains("n_bot") ||
                   id.Contains("skirt") || id.Contains("onep") || id.Contains("onepiece") ||
                   id.Contains("bra") || id.Contains("shorts") || id.Contains("panst") ||
                   id.Contains("socks") || id.Contains("sock") || id.Contains("shocks") ||
                   id.Contains("tights") || id.Contains("stocking") || id.Contains("stockings") ||
                   id.Contains("pants") || id.Contains("shirt") || id.Contains("camisole") ||
                   id.Contains("fincent") || id.Contains("sailor") || id.Contains("cloth") ||
                   id.Contains("/co_") || id.Contains("\\co_") || id.Contains("costume");
        }

        private static bool LooksAccessoryLikeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.Contains("acs") || id.Contains("accessory") || id.Contains("accessories") ||
                   id.Contains("accessary") || id.Contains("acc_") || id.Contains("_acc") ||
                   id.Contains("belt") || id.Contains("waist") || id.Contains("belly") ||
                   id.Contains("navel") || id.Contains("pierce") || id.Contains("ring") ||
                   id.Contains("ribbon") || id.Contains("charm") || id.Contains("chain");
        }

        private static bool IsBellyOrWaistBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("waist") || lower.Contains("spine") ||
                   lower.Contains("hip") || lower.Contains("hips") ||
                   lower.Contains("belly") || lower.Contains("pelvis");
        }

        private static bool IsLegBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            if (IsHipEdgeBoneName(lower)) return false;
            return lower.Contains("thigh") ||
                   (lower.Contains("leg") && !lower.Contains("spine")) ||
                   lower.Contains("knee");
        }

        private static bool IsHipEdgeBoneName(string lower)
        {
            if (string.IsNullOrEmpty(lower)) return false;
            return lower.Contains("hipleg")
                || lower.Contains("hip_leg")
                || lower.Contains("hip-leg")
                || lower.Contains("hip leg");
        }

        private static bool PathHasBellyAnchor(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.Contains("waist") || lower.Contains("spine") ||
                   lower.Contains("hip") || lower.Contains("hips") ||
                   lower.Contains("belly") || lower.Contains("pelvis") ||
                   lower.Contains("cf_j_waist") || lower.Contains("cf_s_waist") ||
                   lower.Contains("cf_j_spine") || lower.Contains("cf_s_spine");
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
                    foreach (var s in all) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body" && s.gameObject.activeInHierarchy)  { RuntimeLogInfo($"[VtxMorph] FindBodySMR: active o_body"); return s; } }
                    foreach (var s in all) { if (s?.sharedMesh == null || !s.sharedMesh.isReadable) continue; if (s.name == "o_body") { RuntimeLogInfo($"[VtxMorph] FindBodySMR: inactive o_body"); return s; } }
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
            if (best != null) RuntimeLogInfo($"[VtxMorph] FindBodySMR: fallback \"{best.name}\" ({bestV}v)");
            return best;
        }

        private static bool IsFaceMesh(SkinnedMeshRenderer smr)
        {
            string id = ((smr.name ?? "") + "/" + (smr.gameObject?.name ?? "")).ToLowerInvariant();
            foreach (var kw in FaceKeywords) if (id.Contains(kw)) return true;
            return false;
        }

        // ── Bone discovery ────────────────────────────────────────────────

        private static bool TryFindBones(Human human, SkinnedMeshRenderer smr, CharaState st, bool verbose = true)
        {
            st.PelvisTf = st.SpineTf = st.LThighTf = st.RThighTf = null;
            st.PelvisIdx = st.SpineIdx = st.LThighIdx = st.RThighIdx = -1;
            st.BellyBoneIdxSet = null;
            st.LegBoneIdxSet   = null;

            // Step 1: Human hierarchy (reliable)
            try
            {
                var allTf = human.gameObject.GetComponentsInChildren<Transform>(true);
                if (verbose) RuntimeLogInfo($"[VtxMorph] TryFindBones: scanning {allTf?.Count ?? 0} transforms");
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
            catch (Exception e) { if (verbose) Log.LogWarning("[VtxMorph] TryFindBones hierarchy: " + e.Message); }

            if (st.PelvisTf == null || st.SpineTf == null)
            {
                if (verbose) Log.LogWarning($"[VtxMorph] TryFindBones: pelvis={st.PelvisTf?.name ?? "null"} spine={st.SpineTf?.name ?? "null"}");
                return false;
            }
            if (verbose) RuntimeLogInfo($"[VtxMorph] TryFindBones: pelvis={st.PelvisTf.name} spine={st.SpineTf.name}");

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
                    if (verbose) RuntimeLogInfo(sb2.ToString());

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
                        // Leg blocker: strong thigh/knee/lower-leg bones only.
                        // cf_s_hipleg* sits on the waist/thigh border and must remain
                        // eligible for clothes; treating it as a leg blocker clipped
                        // bottom/pantyhose edge vertices out of the deformation.
                        if (IsLegBoneName(bn))
                            st.LegBoneIdxSet.Add(i);
                    }

                    if (verbose)
                        RuntimeLogInfo($"[VtxMorph] TryFindBones smr.bones={bc}: " +
                                       $"pelvisIdx={st.PelvisIdx} spineIdx={st.SpineIdx} " +
                                       $"bellySet={st.BellyBoneIdxSet.Count} legSet={st.LegBoneIdxSet.Count}");
                }
                else
                {
                    if (verbose) Log.LogWarning("[VtxMorph] TryFindBones: smr.bones empty — no bindpose / weight filter");
                }
            }
            catch (Exception e)
            {
                if (verbose) Log.LogWarning("[VtxMorph] TryFindBones smr.bones: " + e.Message);
            }

            return true;
        }

        // ── Frame construction ────────────────────────────────────────────
        //
        // Preferred: bindposes (pose-independent).
        // Fallback:  live bone positions via InverseTransformPoint (pose-dependent).

        private static bool BuildFrame(CharaState st, SkinnedMeshRenderer smr, out LocalFrame frame, bool verbose = true)
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
                    if (verbose) Log.LogWarning("[VtxMorph] BuildFrame: LIVE FALLBACK (pose-dependent)");
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
                if (verbose) RuntimeLogInfo($"[VtxMorph] BuildFrame: center={center} boneLen={boneLen:F4} bindpose={bindPoseUsed}");
                return true;
            }
            catch (Exception e) { if (verbose) Log.LogWarning("[VtxMorph] BuildFrame: " + e.Message); return false; }
        }

        private static void ApplyBodyLayerSMRs(
            Human human,
            int charaId,
            List<MeshRecord> recs,
            CharaState st,
            float rate)
        {
            try
            {
                if (st.BodyLayerEntries == null)
                {
                    st.BodyLayerEntries = new List<BodyLayerEntry>();
                    var all = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (all == null) return;

                    int seen = 0, skipped = 0;
                    foreach (var smr in all)
                    {
                        if (!IsBodyLayerSMR(smr, st.SMR)) continue;
                        seen++;

                        var tmp = new CharaState();
                        if (!TryFindBones(human, smr, tmp, false))
                        {
                            skipped++;
                            continue;
                        }

                        st.BodyLayerEntries.Add(new BodyLayerEntry
                        {
                            SMR = smr,
                            BellyBoneIdxSet = tmp.BellyBoneIdxSet,
                            LegBoneIdxSet = tmp.LegBoneIdxSet,
                        });
                    }

                    RuntimeLogInfo($"[VtxMorph] BodyLayerCache id={charaId}: cached={st.BodyLayerEntries.Count}/{seen} skipped={skipped}");
                }

                int applied = 0;
                foreach (var entry in st.BodyLayerEntries)
                {
                    var smr = entry?.SMR;
                    if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                    ApplySMR(recs, smr, st.Frame, rate, entry.BellyBoneIdxSet, entry.LegBoneIdxSet, false, 1f, null);
                    applied++;
                }

                if (applied != st.LastBodyLayerApplied)
                {
                    RuntimeLogInfo($"[VtxMorph] ApplyBodyLayerSMRs id={charaId}: applied={applied}/{st.BodyLayerEntries.Count} rate={rate:F3}");
                    st.LastBodyLayerApplied = applied;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ApplyBodyLayerSMRs: " + e.Message);
            }
        }

        private static void ApplyClothSMRs(
            Human human,
            int charaId,
            List<MeshRecord> recs,
            CharaState st,
            float rate,
            BodyAnchorContext bodyAnchor)
        {
            try
            {
                if (st.ClothEntries == null)
                {
                    st.ClothEntries = new List<ClothEntry>();
                    var all = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (all == null) return;

                    int seen = 0, skipped = 0;
                    foreach (var smr in all)
                    {
                        if (!IsBodyMorphClothSMR(smr, st.SMR)) continue;
                        seen++;

                        var tmp = new CharaState();
                        if (!TryFindBones(human, smr, tmp, false))
                        {
                            skipped++;
                            continue;
                        }

                        st.ClothEntries.Add(new ClothEntry
                        {
                            SMR = smr,
                            BellyBoneIdxSet = tmp.BellyBoneIdxSet,
                            LegBoneIdxSet = tmp.LegBoneIdxSet,
                            Kind = ClassifyClothSMR(smr)
                        });
                    }

                    RuntimeLogInfo($"[VtxMorph] ClothCache id={charaId}: cached={st.ClothEntries.Count}/{seen} skipped={skipped}");
                }

                int applied = 0;
                foreach (var entry in st.ClothEntries)
                {
                    var smr = entry?.SMR;
                    if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                    ApplySMR(recs, smr, st.Frame, rate, entry.BellyBoneIdxSet, entry.LegBoneIdxSet, true, ClothMultiplier(entry.Kind), bodyAnchor, st.SMR.transform);
                    applied++;
                }

                if (applied != st.LastClothApplied)
                {
                    RuntimeLogInfo($"[VtxMorph] ApplyClothSMRs id={charaId}: applied={applied}/{st.ClothEntries.Count} rate={rate:F3}");
                    st.LastClothApplied = applied;
                }

                if (bodyAnchor != null && bodyAnchor.ClothQueries > 0)
                {
                    RuntimeLogInfo($"[VtxMorph] BodyAnchorCloth id={charaId}: hits={bodyAnchor.ClothHits}/{bodyAnchor.ClothQueries} misses={bodyAnchor.ClothMisses} reject={bodyAnchor.ClothRejected} anchors={bodyAnchor.Points.Count}");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ApplyClothSMRs: " + e.Message);
            }
        }

        private static bool IsBodyMorphClothSMR(SkinnedMeshRenderer smr, SkinnedMeshRenderer bodySMR)
        {
            try
            {
                if (smr == null || smr.sharedMesh == null) return false;
                if (bodySMR != null && smr == bodySMR) return false;
                if (bodySMR?.sharedMesh != null && smr.sharedMesh == bodySMR.sharedMesh) return false;
                if (!smr.sharedMesh.isReadable || smr.sharedMesh.vertexCount <= 0) return false;
                if (IsFaceMesh(smr)) return false;

                string id = ((smr.name ?? "") + "/" + (smr.sharedMesh.name ?? "") + "/" + PathOf(smr.transform)).ToLowerInvariant();
                if (id.Contains("nail") || id.Contains("shoe") ||
                    id.Contains("glove") || id.Contains("hair") ||
                    id.Contains("hand") || id.Contains("head") || id.Contains("face") ||
                    id.Contains("o_tang") || id.Contains("dankon") || id.Contains("gomu"))
                    return false;

                return id.Contains("o_top") || id.Contains("/top/") || id.Contains("n_top") ||
                       id.Contains("o_bot") || id.Contains("/bot/") || id.Contains("n_bot") ||
                       id.Contains("skirt") || id.Contains("onep") || id.Contains("onepiece") ||
                       id.Contains("bra") || id.Contains("shorts") || id.Contains("panst") ||
                       id.Contains("socks") || id.Contains("sock") || id.Contains("shocks") ||
                       id.Contains("tights") || id.Contains("stocking") || id.Contains("stockings") ||
                       id.Contains("garter") || id.Contains("swim") || id.Contains("mizugi") ||
                       id.Contains("bikini") || id.Contains("leotard") || id.Contains("inner") ||
                       id.Contains("underwear") ||
                       id.Contains("pants") || id.Contains("shirt") || id.Contains("camisole") ||
                       id.Contains("fincent") || id.Contains("sailor");
            }
            catch { return false; }
        }

        private static bool IsBodyLayerSMR(SkinnedMeshRenderer smr, SkinnedMeshRenderer bodySMR)
        {
            try
            {
                if (smr == null || smr.sharedMesh == null) return false;
                if (bodySMR != null && smr == bodySMR) return false;
                if (bodySMR?.sharedMesh != null && smr.sharedMesh == bodySMR.sharedMesh) return false;
                if (!smr.sharedMesh.isReadable || smr.sharedMesh.vertexCount <= 0) return false;
                if (IsFaceMesh(smr)) return false;
                if (IsBodyMorphClothSMR(smr, bodySMR)) return false;

                string id = ((smr.name ?? "") + "/" + (smr.sharedMesh.name ?? "") + "/" + PathOf(smr.transform)).ToLowerInvariant();
                if (id.Contains("o_hit_") || id.Contains("/n_cm_hit/") || id.Contains("/n_cf_hit/"))
                    return false;

                return id.Contains("nail")
                    || id.Contains("/n_mnp")
                    || id.Contains("mnpa")
                    || id.Contains("mnpb")
                    || id.Contains("pubic")
                    || id.Contains("underhair");
            }
            catch { return false; }
        }

        private static ClothKind ClassifyClothSMR(SkinnedMeshRenderer smr)
        {
            try
            {
                string id = ((smr?.name ?? "") + "/" + (smr?.sharedMesh?.name ?? "") + "/" + PathOf(smr?.transform)).ToLowerInvariant();
                if (id.Contains("panst") || id.Contains("socks") || id.Contains("sock") ||
                    id.Contains("shocks") || id.Contains("tights") ||
                    id.Contains("stocking") || id.Contains("stockings") || id.Contains("garter"))
                    return ClothKind.Panst;
                if (id.Contains("shorts")) return ClothKind.Shorts;
                if (id.Contains("bra") || id.Contains("swim") || id.Contains("mizugi") ||
                    id.Contains("bikini") || id.Contains("leotard"))
                    return ClothKind.Bra;
                if (id.Contains("onep") || id.Contains("onepiece") ||
                    id.Contains("o_top") || id.Contains("/top/") || id.Contains("n_top") ||
                    id.Contains("shirt") || id.Contains("camisole") || id.Contains("fincent") || id.Contains("sailor"))
                    return ClothKind.Top;
                if (id.Contains("o_bot") || id.Contains("/bot/") || id.Contains("n_bot") ||
                    id.Contains("skirt") || id.Contains("pants"))
                    return ClothKind.Bottom;
            }
            catch { }
            return ClothKind.Other;
        }

        private static float ClothMultiplier(ClothKind kind)
        {
            var p = BellyDeformSettings.Vtx;
            return kind switch
            {
                ClothKind.Top    => p.ClothTopMult,
                ClothKind.Bottom => p.ClothBotMult,
                ClothKind.Bra    => p.ClothBraMult,
                ClothKind.Shorts => p.ClothShortsMult,
                ClothKind.Panst  => p.ClothPanstMult,
                _                => p.ClothOtherMult,
            };
        }

        // ── Belly-bound accessories ──────────────────────────────────────

        private static void ApplyAccessories(
            Human human,
            int charaId,
            List<MeshRecord> recs,
            CharaState st,
            float rate,
            BodyAnchorContext bodyAnchor)
        {
            try
            {
                if (human == null || st == null || st.SMR == null || bodyAnchor == null || bodyAnchor.Points.Count == 0)
                    return;

                if (st.AccessoryEntries == null)
                {
                    st.AccessoryEntries = BuildAccessoryEntries(human, st);
                    RuntimeLogInfo($"[VtxMorph] AccessoryCache id={charaId}: cached={st.AccessoryEntries.Count}");
                }

                int applied = 0;
                int skipped = 0;
                foreach (var entry in st.AccessoryEntries)
                {
                    if (entry == null || entry.Mesh == null || entry.Transform == null)
                    {
                        skipped++;
                        continue;
                    }

                    bool ok = entry.Mode == AccessoryMode.WaistSurface
                        ? ApplyAccessorySurface(recs, entry, st, bodyAnchor)
                        : ApplyAccessoryRigid(recs, entry, st, bodyAnchor);
                    if (ok) applied++;
                    else skipped++;
                }

                if (applied != st.LastAccessoryApplied)
                {
                    RuntimeLogInfo($"[VtxMorph] ApplyAccessories id={charaId}: applied={applied}/{st.AccessoryEntries.Count} skipped={skipped} rate={rate:F3}");
                    st.LastAccessoryApplied = applied;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ApplyAccessories: " + e.Message);
            }
        }

        private static List<AccessoryEntry> BuildAccessoryEntries(Human human, CharaState st)
        {
            var entries = new List<AccessoryEntry>();
            var seen = new HashSet<int>();
            try
            {
                var mfs = human.gameObject.GetComponentsInChildren<MeshFilter>(true);
                if (mfs != null)
                {
                    foreach (var mf in mfs)
                    {
                        if (mf == null || mf.sharedMesh == null || mf.transform == null) continue;
                        if (!TryGetAccessoryMode(mf.transform, out AccessoryMode mode, out Transform anchor, out string anchorName)) continue;
                        Transform applyTf = FindAccessoryControlTransform(mf.transform, anchor);
                        int key = mode == AccessoryMode.RigidMove && applyTf != null ? applyTf.GetInstanceID() : mf.GetInstanceID();
                        if (!seen.Add(key)) continue;
                        entries.Add(new AccessoryEntry
                        {
                            Component = mf,
                            Mesh = mf.sharedMesh,
                            Transform = mf.transform,
                            ApplyTransform = applyTf,
                            Mode = mode,
                            AnchorName = anchorName,
                            IsSkinned = false,
                        });
                    }
                }

                var smrs = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs != null)
                {
                    foreach (var smr in smrs)
                    {
                        if (smr == null || smr.sharedMesh == null || smr.transform == null) continue;
                        if (smr == st.SMR || smr.sharedMesh == st.SMR.sharedMesh) continue;
                        if (IsBodyMorphClothSMR(smr, st.SMR)) continue;
                        if (!TryGetAccessoryMode(smr.transform, out AccessoryMode mode, out Transform anchor, out string anchorName)) continue;
                        Transform applyTf = FindAccessoryControlTransform(smr.transform, anchor);
                        Mesh mesh = smr.sharedMesh;
                        if (mode == AccessoryMode.WaistSurface && !MeshReadable(mesh))
                        {
                            string originalName = mesh?.name ?? "null";
                            if (TryBuildReadableAccessorySurfaceMesh(smr, mesh, out Mesh replacement, out string detail))
                            {
                                smr.sharedMesh = replacement;
                                mesh = replacement;
                                RuntimeLogInfo($"[VtxMorph] AccessoryReadableReplace ok anchor={anchorName} smr=\"{smr.name}\" original=\"{originalName}\" {detail} path=\"{PathOf(smr.transform)}\"");
                            }
                            else
                            {
                                RuntimeLogInfo($"[VtxMorph] AccessoryReadableReplace fail anchor={anchorName} smr=\"{smr.name}\" mesh=\"{mesh?.name}\" {detail} path=\"{PathOf(smr.transform)}\"");
                            }
                        }
                        int key = mode == AccessoryMode.RigidMove && applyTf != null ? applyTf.GetInstanceID() : smr.GetInstanceID();
                        if (!seen.Add(key)) continue;
                        entries.Add(new AccessoryEntry
                        {
                            Component = smr,
                            Mesh = mesh,
                            Transform = smr.transform,
                            ApplyTransform = applyTf,
                            Mode = mode,
                            AnchorName = anchorName,
                            IsSkinned = true,
                        });
                    }
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    string meshInfo = e.Mesh != null ? $"{e.Mesh.name} verts={e.Mesh.vertexCount} readable={MeshReadable(e.Mesh)}" : "mesh=null";
                    RuntimeLogInfo($"[VtxMorph] AccessoryEntry[{i}] anchor={e.AnchorName} mode={e.Mode} skinned={e.IsSkinned} {meshInfo} tf=\"{PathOf(e.Transform)}\" applyTf=\"{PathOf(e.ApplyTransform)}\"");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] BuildAccessoryEntries: " + e.Message);
            }
            return entries;
        }

        private static bool TryGetAccessoryMode(Transform t, out AccessoryMode mode)
        {
            return TryGetAccessoryMode(t, out mode, out _, out _);
        }

        private static bool TryGetAccessoryMode(Transform t, out AccessoryMode mode, out Transform anchor, out string anchorName)
        {
            mode = AccessoryMode.WaistSurface;
            anchor = null;
            anchorName = "";
            try
            {
                for (var c = t; c != null; c = c.parent)
                {
                    string name = (c.name ?? "").ToLowerInvariant();
                    if (name == "a_n_waist_f" || name == "a_n_dan")
                    {
                        mode = AccessoryMode.RigidMove;
                        anchor = c;
                        anchorName = name;
                        return true;
                    }
                    if (name == "a_n_waist")
                    {
                        mode = AccessoryMode.WaistSurface;
                        anchor = c;
                        anchorName = name;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static Transform FindAccessoryControlTransform(Transform renderTf, Transform anchor)
        {
            try
            {
                if (renderTf == null) return null;
                if (anchor == null) return renderTf;
                Transform directChild = null;
                Transform nc = null;
                for (var c = renderTf; c != null; c = c.parent)
                {
                    string name = (c.name ?? "").ToLowerInvariant();
                    if (name == "n_move")
                        return c;
                    if (nc == null && name == "nc")
                        nc = c;
                    if (c.parent == anchor)
                        directChild = c;
                    if (c == anchor)
                        break;
                }
                if (nc != null) return nc;
                if (directChild != null) return directChild;
            }
            catch { }
            return renderTf;
        }

        private static bool TryBuildReadableAccessorySurfaceMesh(
            SkinnedMeshRenderer smr,
            Mesh original,
            out Mesh replacement,
            out string detail)
        {
            Mesh baked = null;
            replacement = null;
            detail = "";
            try
            {
                if (smr == null || original == null)
                {
                    detail = "null";
                    return false;
                }

                baked = new Mesh();
                smr.BakeMesh(baked);

                Vector3[] verts = baked.vertices;
                int[] tris = baked.triangles;
                if (verts == null || verts.Length == 0 || tris == null || tris.Length == 0)
                {
                    detail = $"bake-empty verts={verts?.Length ?? -1} tris={tris?.Length ?? -1}";
                    return false;
                }

                BoneWeight[] boneWeights = original.boneWeights;
                if (boneWeights == null || boneWeights.Length != verts.Length)
                {
                    detail = $"boneWeights-len={boneWeights?.Length ?? -1} verts={verts.Length}";
                    return false;
                }

                Matrix4x4[] bindposes = original.bindposes;
                if (bindposes == null || bindposes.Length == 0)
                {
                    detail = "bindposes-empty";
                    return false;
                }

                Mesh mesh = new Mesh();
                mesh.name = original.name;
                mesh.hideFlags = HideFlags.DontSave;
                mesh.vertices = verts;
                mesh.triangles = tris;
                CopyVector3ArrayIfLength(baked.normals, verts.Length, a => mesh.normals = a);
                CopyVector4ArrayIfLength(baked.tangents, verts.Length, a => mesh.tangents = a);
                CopyVector2ArrayIfLength(baked.uv, verts.Length, a => mesh.uv = a);
                CopyVector2ArrayIfLength(baked.uv2, verts.Length, a => mesh.uv2 = a);
                mesh.bindposes = bindposes;
                mesh.boneWeights = boneWeights;
                try { mesh.RecalculateBounds(); } catch { }

                replacement = mesh;
                detail = $"bakedVerts={verts.Length} tris={tris.Length} bindposes={bindposes.Length} boneWeights={boneWeights.Length}";
                return true;
            }
            catch (Exception e)
            {
                detail = "exception:" + e.Message;
                try { if (replacement != null) UnityEngine.Object.Destroy(replacement); } catch { }
                replacement = null;
                return false;
            }
            finally
            {
                try { if (baked != null) UnityEngine.Object.Destroy(baked); } catch { }
            }
        }

        private static void CopyVector3ArrayIfLength(Vector3[] src, int length, Action<Vector3[]> assign)
        {
            try
            {
                if (src != null && src.Length == length)
                    assign((Vector3[])src.Clone());
            }
            catch { }
        }

        private static void CopyVector4ArrayIfLength(Vector4[] src, int length, Action<Vector4[]> assign)
        {
            try
            {
                if (src != null && src.Length == length)
                    assign((Vector4[])src.Clone());
            }
            catch { }
        }

        private static void CopyVector2ArrayIfLength(Vector2[] src, int length, Action<Vector2[]> assign)
        {
            try
            {
                if (src != null && src.Length == length)
                    assign((Vector2[])src.Clone());
            }
            catch { }
        }

        private static bool ApplyAccessorySurface(
            List<MeshRecord> recs,
            AccessoryEntry entry,
            CharaState st,
            BodyAnchorContext bodyAnchor)
        {
            Mesh mesh = entry.Mesh;
            if (!MeshReadable(mesh))
                return ApplyAccessoryRigid(recs, entry, st, bodyAnchor);

            try
            {
                MeshRecord rec = GetOrCaptureRecord(recs, mesh, out Vector3[] cur);
                if (rec?.OrigVerts == null || cur == null || cur.Length == 0) return false;
                if (bodyAnchor == null || bodyAnchor.Points.Count == 0) return false;

                int n = rec.OrigVerts.Length;
                var newV = new Vector3[n];
                var movedFlags = new bool[n];
                var bodyOrig = new Vector3[n];
                var bodyNew = new Vector3[n];
                int moved = 0;
                Transform bodyTf = st.SMR.transform;
                Transform sourceTf = entry.Transform;
                float minMovedSq = Mathf.Max(bodyAnchor.MinMovedSq, 1e-10f);

                for (int i = 0; i < n; i++)
                {
                    Vector3 originalLocal = rec.OrigVerts[i];
                    Vector3 originalBody = ToBodyLocal(sourceTf, bodyTf, originalLocal);
                    bodyOrig[i] = originalBody;
                    bodyNew[i] = originalBody;
                    if (!TryGetBodyAnchorClothTarget(bodyAnchor, originalBody, 1f, out Vector3 targetBody))
                    {
                        newV[i] = originalLocal;
                        continue;
                    }

                    bodyNew[i] = targetBody;
                    movedFlags[i] = true;
                    if ((targetBody - originalBody).sqrMagnitude >= minMovedSq)
                        moved++;
                }

                if (moved == 0)
                {
                    RestoreRecordIfApplied(rec);
                    return false;
                }

                if (rec.Neighbors == null)
                    rec.Neighbors = BuildMeshNeighbors(mesh, n);
                RepairClothDistortion(bodyOrig, bodyNew, movedFlags, st.Frame, rec.Neighbors);

                for (int i = 0; i < n; i++)
                    newV[i] = movedFlags[i] ? FromBodyLocal(sourceTf, bodyTf, bodyNew[i]) : rec.OrigVerts[i];

                rec.AppliedSig = Sig(newV);
                rec.LastNewV = newV;
                mesh.vertices = newV;
                try { mesh.RecalculateNormals(); } catch { }
                try { mesh.RecalculateTangents(); } catch { }
                mesh.RecalculateBounds();
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ApplyAccessorySurface: " + e.Message);
                return false;
            }
        }

        private static bool ApplyAccessoryRigid(
            List<MeshRecord> recs,
            AccessoryEntry entry,
            CharaState st,
            BodyAnchorContext bodyAnchor)
        {
            try
            {
                if (entry == null || entry.Transform == null || st?.SMR == null || bodyAnchor == null || bodyAnchor.Points.Count == 0)
                    return false;

                Transform sourceTf = entry.ApplyTransform != null ? entry.ApplyTransform : entry.Transform;
                Transform bodyTf = st.SMR.transform;
                if (!TryGetAccessoryRigidSampleBody(entry, bodyTf, bodyAnchor, out Vector3 sampleBody))
                {
                    RestoreAccessoryTransform(st, sourceTf);
                    return false;
                }

                bool requireAffectedAnchor = entry.Mode == AccessoryMode.RigidMove;
                if (!TryGetBodyAnchorRigidTarget(bodyAnchor, sampleBody, requireAffectedAnchor, out Vector3 targetBody, out Quaternion bodyRotDelta))
                {
                    RestoreAccessoryTransform(st, sourceTf);
                    return false;
                }

                Vector3 bodyDelta = targetBody - sampleBody;
                if (bodyDelta.sqrMagnitude < Mathf.Max(bodyAnchor.MinMovedSq, 1e-10f))
                {
                    RestoreAccessoryTransform(st, sourceTf);
                    return false;
                }

                ApplyAccessoryTransformPose(st, sourceTf, bodyTf, sampleBody, targetBody, bodyRotDelta);
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ApplyAccessoryRigid: " + e.Message);
                return false;
            }
        }

        private static MeshRecord GetOrCaptureRecord(List<MeshRecord> recs, Mesh mesh, out Vector3[] cur)
        {
            cur = null;
            if (recs == null || mesh == null) return null;

            MeshRecord rec = FindRecord(recs, mesh);
            cur = mesh.vertices;
            if (cur == null || cur.Length == 0) return rec;

            bool wasApplied = rec != null && rec.AppliedSig != 0
                && cur.Length == rec.OrigVerts?.Length && Sig(cur) == rec.AppliedSig;

            if (rec == null)
            {
                rec = new MeshRecord { Mesh = mesh, OrigVerts = (Vector3[])cur.Clone() };
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
            return rec;
        }

        private static void RestoreRecordIfApplied(MeshRecord rec)
        {
            if (rec == null) return;
            UndoRecord(rec);
        }

        private static void ApplyAccessoryTransformPose(
            CharaState st,
            Transform sourceTf,
            Transform bodyTf,
            Vector3 sampleBody,
            Vector3 targetBody,
            Quaternion bodyRotDelta)
        {
            if (st?.AccessoryTransformRecords == null || sourceTf == null || bodyTf == null) return;

            if (!st.AccessoryTransformRecords.TryGetValue(sourceTf, out TransformRecord rec)
                || rec == null
                || rec.Parent != sourceTf.parent)
            {
                rec = new TransformRecord
                {
                    Transform = sourceTf,
                    Parent = sourceTf.parent,
                    OrigLocalPosition = sourceTf.localPosition,
                    LastAppliedLocalPosition = sourceTf.localPosition,
                    OrigLocalRotation = sourceTf.localRotation,
                    LastAppliedLocalRotation = sourceTf.localRotation,
                    Applied = false,
                };
                st.AccessoryTransformRecords[sourceTf] = rec;
            }
            else if (!rec.Applied)
            {
                rec.OrigLocalPosition = sourceTf.localPosition;
                rec.LastAppliedLocalPosition = sourceTf.localPosition;
                rec.OrigLocalRotation = sourceTf.localRotation;
                rec.LastAppliedLocalRotation = sourceTf.localRotation;
            }

            Vector3 sampleWorld = bodyTf.TransformPoint(sampleBody);
            Vector3 targetWorld = bodyTf.TransformPoint(targetBody);
            Vector3 originalWorldPosition = rec.Parent != null
                ? rec.Parent.TransformPoint(rec.OrigLocalPosition)
                : rec.OrigLocalPosition;
            Quaternion targetLocalRotation = rec.OrigLocalRotation;
            Quaternion worldRotDelta = Quaternion.identity;

            if (IsFinite(bodyRotDelta))
            {
                try
                {
                    Quaternion bodyWorldRot = bodyTf.rotation;
                    worldRotDelta = bodyWorldRot * bodyRotDelta * Quaternion.Inverse(bodyWorldRot);
                    Quaternion originalWorldRotation = rec.Parent != null
                        ? rec.Parent.rotation * rec.OrigLocalRotation
                        : rec.OrigLocalRotation;
                    Quaternion targetWorldRotation = worldRotDelta * originalWorldRotation;
                    targetLocalRotation = rec.Parent != null
                        ? Quaternion.Inverse(rec.Parent.rotation) * targetWorldRotation
                        : targetWorldRotation;
                    if (!IsFinite(targetLocalRotation))
                        targetLocalRotation = rec.OrigLocalRotation;
                }
                catch
                {
                    targetLocalRotation = rec.OrigLocalRotation;
                }
            }

            Vector3 targetWorldPosition = targetWorld - worldRotDelta * (sampleWorld - originalWorldPosition);
            Vector3 targetLocal = rec.Parent != null
                ? rec.Parent.InverseTransformPoint(targetWorldPosition)
                : targetWorldPosition;

            sourceTf.localPosition = targetLocal;
            sourceTf.localRotation = targetLocalRotation;
            rec.LastAppliedLocalPosition = targetLocal;
            rec.LastAppliedLocalRotation = targetLocalRotation;
            rec.Applied = true;
        }

        private static void RestoreAccessoryTransform(CharaState st, Transform sourceTf)
        {
            try
            {
                if (st?.AccessoryTransformRecords == null || sourceTf == null) return;
                if (!st.AccessoryTransformRecords.TryGetValue(sourceTf, out TransformRecord rec)) return;
                if (rec == null || !rec.Applied) return;
                sourceTf.localPosition = rec.OrigLocalPosition;
                sourceTf.localRotation = rec.OrigLocalRotation;
                rec.LastAppliedLocalPosition = rec.OrigLocalPosition;
                rec.LastAppliedLocalRotation = rec.OrigLocalRotation;
                rec.Applied = false;
            }
            catch { }
        }

        private static void UndoAccessoryTransforms(CharaState st)
        {
            try
            {
                if (st?.AccessoryTransformRecords == null) return;
                foreach (var rec in st.AccessoryTransformRecords.Values)
                {
                    if (rec?.Transform == null || !rec.Applied) continue;
                    try { rec.Transform.localPosition = rec.OrigLocalPosition; } catch { }
                    try { rec.Transform.localRotation = rec.OrigLocalRotation; } catch { }
                    rec.Applied = false;
                }
                st.AccessoryTransformRecords.Clear();
            }
            catch { }
        }

        private static bool TryGetAccessoryRigidSampleBody(
            AccessoryEntry entry,
            Transform bodyTf,
            BodyAnchorContext bodyAnchor,
            out Vector3 sampleBody)
        {
            sampleBody = Vector3.zero;
            try
            {
                if (entry == null || bodyTf == null || bodyAnchor == null || bodyAnchor.Points.Count == 0)
                    return false;
                if (entry.RigidSampleValid)
                {
                    sampleBody = entry.RigidSampleBody;
                    return true;
                }
                if (!TryGetAccessoryVertexSnapshot(entry, out Vector3[] vertices, out Transform vertexTf))
                    return false;

                bool found = false;
                float bestSq = float.MaxValue;
                Vector3 best = Vector3.zero;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 bodyPoint = ToBodyLocal(vertexTf, bodyTf, vertices[i]);
                    if (!IsFinite(bodyPoint)) continue;
                    if (!TryFindNearestBodyAnchorPoint(bodyAnchor, bodyPoint, true, out BodyAnchorPoint anchor))
                        continue;
                    float sq = (anchor.Original - bodyPoint).sqrMagnitude;
                    if (sq >= bestSq) continue;
                    bestSq = sq;
                    best = bodyPoint;
                    found = true;
                }

                if (!found) return false;
                entry.RigidSampleBody = best;
                entry.RigidSampleValid = true;
                sampleBody = best;
                return true;
            }
            catch
            {
                sampleBody = Vector3.zero;
                return false;
            }
        }

        private static bool TryGetAccessoryVertexSnapshot(
            AccessoryEntry entry,
            out Vector3[] vertices,
            out Transform vertexTf)
        {
            vertices = null;
            vertexTf = null;
            Mesh baked = null;
            try
            {
                if (entry == null) return false;

                if (entry.Component is SkinnedMeshRenderer smr && smr != null)
                {
                    baked = new Mesh();
                    smr.BakeMesh(baked);
                    vertices = baked.vertices;
                    vertexTf = smr.transform;
                    return vertices != null && vertices.Length > 0 && vertexTf != null;
                }

                if (entry.Component is MeshFilter mf && mf != null && MeshReadable(mf.sharedMesh))
                {
                    vertices = mf.sharedMesh.vertices;
                    vertexTf = mf.transform;
                    return vertices != null && vertices.Length > 0 && vertexTf != null;
                }

                if (MeshReadable(entry.Mesh) && entry.Transform != null)
                {
                    vertices = entry.Mesh.vertices;
                    vertexTf = entry.Transform;
                    return vertices != null && vertices.Length > 0;
                }
            }
            catch
            {
                vertices = null;
                vertexTf = null;
                return false;
            }
            finally
            {
                try { if (baked != null) UnityEngine.Object.Destroy(baked); } catch { }
            }

            return false;
        }

        private static Vector3 AccessorySampleBody(AccessoryEntry entry, Transform bodyTf, Transform applyTf)
        {
            try
            {
                if (entry != null && entry.Mode == AccessoryMode.RigidMove && applyTf != null)
                    return bodyTf.InverseTransformPoint(applyTf.position);

                var renderer = entry.Component != null
                    ? entry.Component.GetComponent<Renderer>()
                    : entry.Transform.GetComponent<Renderer>();
                if (renderer != null)
                    return bodyTf.InverseTransformPoint(renderer.bounds.center);
            }
            catch { }
            return bodyTf.InverseTransformPoint(entry.Transform.position);
        }

        private static Vector3 BoundsCenter(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return Vector3.zero;
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            return (min + max) * 0.5f;
        }

        private static Vector3 ToBodyLocal(Transform sourceTf, Transform bodyTf, Vector3 sourceLocal)
        {
            return bodyTf.InverseTransformPoint(sourceTf.TransformPoint(sourceLocal));
        }

        private static Vector3 FromBodyLocal(Transform sourceTf, Transform bodyTf, Vector3 bodyLocal)
        {
            return sourceTf.InverseTransformPoint(bodyTf.TransformPoint(bodyLocal));
        }

        private static bool MeshReadable(Mesh mesh)
        {
            try { return mesh != null && mesh.isReadable; }
            catch { return false; }
        }

        // ── Per-SMR deformation ───────────────────────────────────────────

        private static void ApplySMR(
            List<MeshRecord> recs,
            SkinnedMeshRenderer smr,
            LocalFrame fr,
            float rate,
            HashSet<int> bellyBoneSet,
            HashSet<int> legBoneSet,
            bool isCloth,
            float clothMultiplier,
            BodyAnchorContext bodyAnchor,
            Transform bodyTransform = null)
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
                    // Body keeps PP-style bone filtering. Clothes use the original
                    // geometric selection, then only actually moved vertices enter
                    // the cloth/body-anchor cleanup path.
                    rec.BellyMask     = isCloth ? null : ComputeBellyMask(mesh, cur.Length, bellyBoneSet, legBoneSet);
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
                bool[] moved = isCloth ? new bool[n] : null;
                Vector3[] bodyOrig = isCloth ? new Vector3[n] : null;
                Vector3[] bodyNew  = isCloth ? new Vector3[n] : null;
                Transform sourceTransform = null;
                try { sourceTransform = smr.transform; } catch { }
                Transform morphTransform = bodyTransform != null ? bodyTransform : sourceTransform;
                bool useBodySpace = isCloth && sourceTransform != null && morphTransform != null && sourceTransform != morphTransform;
                int deformed = 0;
                bool[] mask = rec.BellyMask;

                for (int i = 0; i < n; i++)
                {
                    Vector3 sourceLv = rec.OrigVerts[i];
                    Vector3 lv = useBodySpace ? ToBodyLocal(sourceTransform, morphTransform, sourceLv) : sourceLv;
                    newV[i] = sourceLv;
                    if (bodyOrig != null)
                    {
                        bodyOrig[i] = lv;
                        bodyNew[i] = lv;
                    }

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
                    if (e >= 1f)
                        continue;

                    float sqrtE = Mathf.Sqrt(e);

                    // ── EdgeSmooth (inner boundary falloff) ───────────────
                    float coreR = 1f - Mathf.Clamp01(p.EdgeSmooth) * 0.5f;
                    float tEdge = Mathf.Clamp01((sqrtE - coreR) / Mathf.Max(1f - coreR, 1e-4f));
                    float str   = rate * (1f - tEdge * tEdge * (3f - 2f * tEdge));

                    // ── RoundToSides (PP mechanism #4) ────────────────────
                    // forwardFromBack: 0 at belly back edge, (rF+rB) at front.
                    // Soft-ramp from 0 → 1 over [0, rtsSmoothDist].
                    float forwardFromBack = fwD + rB;
                    if (forwardFromBack <= 0f)
                        continue; // behind belly, skip
                    float rtsT = forwardFromBack >= rtsSmoothDist ? 1f
                               : Mathf.Clamp01(forwardFromBack / rtsSmoothDist);
                    float rts  = rtsT * rtsT * (3f - 2f * rtsT); // SmoothStep ≈ PP's AnimationCurve
                    str *= rts;

                    if (str <= 1e-5f)
                        continue;

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
                            if (restore >= 1f)
                            {
                                if (bodyNew != null) bodyNew[i] = lv;
                                else newV[i] = lv;
                                continue;
                            }   // fully blocked — no deformation
                            nlv = Vector3.Lerp(nlv, lv, restore);
                        }
                    }

                    if (isCloth)
                    {
                        float clothMul = Mathf.Max(0f, clothMultiplier);
                        if (!Mathf.Approximately(clothMul, 1f))
                            nlv = lv + (nlv - lv) * clothMul;
                        if (bodyAnchor != null
                            && bodyAnchor.SurfaceTriangles.Count > 0
                            && (nlv - lv).sqrMagnitude >= bodyAnchor.MinMovedSq
                            && TryGetBodySurfaceClothTarget(bodyAnchor, lv, clothMul, fr, out Vector3 surfaceTarget))
                        {
                            nlv = surfaceTarget;
                        }
                    }

                    if (bodyNew != null) bodyNew[i] = nlv;
                    else newV[i] = nlv;
                    if (moved != null) moved[i] = true;
                    deformed++;
                }

                int distortionFixed = 0;
                if (isCloth && deformed > 0)
                {
                    if (rec.Neighbors == null)
                        rec.Neighbors = BuildMeshNeighbors(mesh, n);
                    distortionFixed = RepairClothDistortion(bodyOrig, bodyNew, moved, fr, rec.Neighbors);
                }

                if (isCloth && bodyNew != null)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (moved != null && !moved[i])
                        {
                            newV[i] = rec.OrigVerts[i];
                            continue;
                        }
                        newV[i] = useBodySpace ? FromBodyLocal(sourceTransform, morphTransform, bodyNew[i]) : bodyNew[i];
                    }
                }

                // Log only on first deformation or deformed-count changes (not every UI tick)
                if (rec.AppliedSig == 0 || deformed != rec.LastDeformedCount)
                    RuntimeLogInfo($"[VtxMorph] ApplySMR: mesh=\"{mesh.name}\" smr=\"{smr.name}\" cloth={isCloth} {deformed}/{n} verts deformed rate={rate:F3}");
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

        private static MeshRecord FindRecord(List<MeshRecord> recs, Mesh mesh)
        {
            if (recs == null || mesh == null) return null;
            foreach (var r in recs)
                if (r != null && r.Mesh == mesh)
                    return r;
            return null;
        }

        private static BodyAnchorContext CreateBodyAnchorContext(LocalFrame fr)
        {
            float cell = Mathf.Max(fr.BoneLen * 0.25f, 0.005f);
            float minMoved = Mathf.Max(fr.BoneLen * 0.0001f, 0.000001f);
            return new BodyAnchorContext
            {
                CellSize = cell,
                MinMovedSq = minMoved * minMoved,
            };
        }

        private static void FinalizeBodyAnchorContext(BodyAnchorContext context)
        {
            if (context == null || context.Points.Count == 0) return;

            context.Points.Sort((a, b) => b.OriginalUp.CompareTo(a.OriginalUp));
            context.Buckets.Clear();
            for (int i = 0; i < context.Points.Count; i++)
            {
                var point = context.Points[i];
                BodyAnchorBucketCoords(point.Original, context.CellSize, out int bx, out int by, out int bz);
                int key = BodyAnchorBucketKey(bx, by, bz);
                if (!context.Buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    context.Buckets[key] = list;
                }
                list.Add(i);
            }
        }

        private static void AddBodyAnchorMesh(
            BodyAnchorContext context,
            SkinnedMeshRenderer smr,
            MeshRecord rec,
            bool[] mask,
            Vector3[] morphedVerts,
            LocalFrame fr)
        {
            if (context == null || smr == null || smr.sharedMesh == null || rec == null) return;
            if (rec.OrigVerts == null || morphedVerts == null) return;

            Mesh mesh = smr.sharedMesh;
            int count = rec.OrigVerts.Length;
            if (morphedVerts.Length != count) return;

            if (rec.Neighbors == null)
                rec.Neighbors = BuildMeshNeighbors(mesh, count);
            if (rec.Neighbors == null) return;

            var anchorMesh = new BodyAnchorMesh
            {
                Original = new Vector3[count],
                Morphed = new Vector3[count],
                Valid = new bool[count],
                Affected = new bool[count],
                Bases = new BodyAnchorBasis[count],
                SurfaceTrianglesByVertex = new List<int>[count],
            };

            for (int i = 0; i < count; i++)
            {
                anchorMesh.Original[i] = rec.OrigVerts[i];
                anchorMesh.Morphed[i] = morphedVerts[i];
                anchorMesh.Valid[i] = true;
            }

            for (int i = 0; i < count; i++)
            {
                if (!anchorMesh.Valid[i]) continue;
                if ((anchorMesh.Morphed[i] - anchorMesh.Original[i]).sqrMagnitude < context.MinMovedSq) continue;
                anchorMesh.Affected[i] = true;
            }

            int meshIndex = context.Meshes.Count;
            int added = 0;
            int affected = 0;
            for (int i = 0; i < count; i++)
            {
                if (!anchorMesh.Valid[i]) continue;
                if (anchorMesh.Affected[i]) affected++;
                if (!TryBuildBodyAnchorBasis(context, anchorMesh, rec.Neighbors, i, out BodyAnchorBasis basis))
                {
                    context.BasisFailed++;
                    continue;
                }

                anchorMesh.Bases[i] = basis;
                context.Points.Add(new BodyAnchorPoint
                {
                    MeshIndex = meshIndex,
                    VertexIndex = i,
                    Original = anchorMesh.Original[i],
                    OriginalUp = Vector3.Dot(anchorMesh.Original[i] - fr.Center, fr.Up),
                });
                added++;
            }

            int surfaceAdded = 0;
            if (added > 0)
            {
                context.Meshes.Add(anchorMesh);
                surfaceAdded = AddBodySurfaceTriangles(context, mesh, anchorMesh, meshIndex);
            }

            RuntimeLogInfo($"[VtxMorph] BodyAnchor: anchors={added}/{count} affected={affected} surfaceTri={surfaceAdded} direct={context.BasisDirect} second={context.BasisSecondOrder} fail={context.BasisFailed}");
        }

        private static int AddBodySurfaceTriangles(
            BodyAnchorContext context,
            Mesh mesh,
            BodyAnchorMesh anchorMesh,
            int meshIndex)
        {
            if (context == null || mesh == null || anchorMesh == null) return 0;
            if (anchorMesh.Original == null || anchorMesh.Morphed == null || anchorMesh.Valid == null || anchorMesh.Affected == null)
                return 0;

            int[] triangles;
            try { triangles = mesh.triangles; }
            catch { return 0; }
            if (triangles == null || triangles.Length < 3)
                return 0;

            float minEdge = Mathf.Max(context.CellSize * 0.015f, 0.00001f);
            float minArea = minEdge * minEdge;
            float minAreaSq = minArea * minArea;
            int count = anchorMesh.Original.Length;
            int added = 0;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsValidSurfaceTriangleVertex(anchorMesh, a, count)
                    || !IsValidSurfaceTriangleVertex(anchorMesh, b, count)
                    || !IsValidSurfaceTriangleVertex(anchorMesh, c, count))
                    continue;

                // A body surface triangle participates when any of its vertices was moved.
                if (!anchorMesh.Affected[a] && !anchorMesh.Affected[b] && !anchorMesh.Affected[c])
                    continue;

                Vector3 p0 = anchorMesh.Original[a];
                Vector3 p1 = anchorMesh.Original[b];
                Vector3 p2 = anchorMesh.Original[c];
                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (!IsFinite(normal) || normal.sqrMagnitude < minAreaSq)
                    continue;

                Vector3 center = (p0 + p1 + p2) / 3f;
                float radiusSq = (p0 - center).sqrMagnitude;
                radiusSq = Mathf.Max(radiusSq, (p1 - center).sqrMagnitude);
                radiusSq = Mathf.Max(radiusSq, (p2 - center).sqrMagnitude);

                int triIndex = context.SurfaceTriangles.Count;
                context.SurfaceTriangles.Add(new BodySurfaceTriangle
                {
                    MeshIndex = meshIndex,
                    A = a,
                    B = b,
                    C = c,
                    Center = center,
                    RadiusSq = radiusSq,
                });
                AddBodySurfaceTriangleForVertex(anchorMesh, a, triIndex);
                AddBodySurfaceTriangleForVertex(anchorMesh, b, triIndex);
                AddBodySurfaceTriangleForVertex(anchorMesh, c, triIndex);
                AddBodySurfaceTriangleToBuckets(context, triIndex, p0, p1, p2);
                added++;
            }

            return added;
        }

        private static void AddBodySurfaceTriangleForVertex(BodyAnchorMesh mesh, int vertexIndex, int triIndex)
        {
            if (mesh?.SurfaceTrianglesByVertex == null) return;
            if (vertexIndex < 0 || vertexIndex >= mesh.SurfaceTrianglesByVertex.Length) return;
            var list = mesh.SurfaceTrianglesByVertex[vertexIndex];
            if (list == null)
            {
                list = new List<int>(6);
                mesh.SurfaceTrianglesByVertex[vertexIndex] = list;
            }
            list.Add(triIndex);
        }

        private static bool IsValidSurfaceTriangleVertex(BodyAnchorMesh mesh, int index, int count)
        {
            return index >= 0
                && index < count
                && mesh.Valid[index]
                && IsFinite(mesh.Original[index])
                && IsFinite(mesh.Morphed[index]);
        }

        private static void AddBodySurfaceTriangleToBuckets(
            BodyAnchorContext context,
            int triIndex,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2)
        {
            if (context == null) return;

            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x));
            float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y));
            float minZ = Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
            float maxZ = Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z));

            BodyAnchorBucketCoords(new Vector3(minX, minY, minZ), context.CellSize, out int minBx, out int minBy, out int minBz);
            BodyAnchorBucketCoords(new Vector3(maxX, maxY, maxZ), context.CellSize, out int maxBx, out int maxBy, out int maxBz);

            for (int x = minBx; x <= maxBx; x++)
                for (int y = minBy; y <= maxBy; y++)
                    for (int z = minBz; z <= maxBz; z++)
                    {
                        int key = BodyAnchorBucketKey(x, y, z);
                        if (!context.SurfaceBuckets.TryGetValue(key, out var list))
                        {
                            list = new List<int>(4);
                            context.SurfaceBuckets[key] = list;
                        }
                        list.Add(triIndex);
                    }
        }

        private static bool TryBuildBodyAnchorBasis(
            BodyAnchorContext context,
            BodyAnchorMesh mesh,
            List<int>[] neighbors,
            int index,
            out BodyAnchorBasis basis)
        {
            basis = new BodyAnchorBasis();
            if (context == null || mesh == null || mesh.Original == null || mesh.Morphed == null || mesh.Valid == null)
                return false;
            if (neighbors == null || index < 0 || index >= neighbors.Length || !mesh.Valid[index])
                return false;

            List<int> ns = neighbors[index];
            if (ns == null) return false;

            Vector3 p0 = mesh.Original[index];
            Vector3 p0m = mesh.Morphed[index];
            float minEdge = Mathf.Max(context.CellSize * 0.015f, 0.00001f);
            float minArea = minEdge * minEdge;
            var candidates = new List<int>();
            var seen = new HashSet<int>();

            AddAffectedAnchorCandidates(mesh, ns, index, candidates, seen);
            if (candidates.Count >= 2
                && TryCreateRandomAnchorBasisFromCandidates(mesh, index, candidates, p0, p0m, minEdge, minArea, out basis))
            {
                context.BasisDirect++;
                return true;
            }

            for (int n = 0; n < ns.Count; n++)
            {
                int neighbor = ns[n];
                if (neighbor < 0 || neighbor >= neighbors.Length) continue;
                AddAffectedAnchorCandidates(mesh, neighbors[neighbor], index, candidates, seen);
            }

            if (candidates.Count >= 2
                && TryCreateRandomAnchorBasisFromCandidates(mesh, index, candidates, p0, p0m, minEdge, minArea, out basis))
            {
                context.BasisSecondOrder++;
                return true;
            }

            return false;
        }

        private static bool TryCreateRandomAnchorBasisFromCandidates(
            BodyAnchorMesh mesh,
            int index,
            List<int> candidates,
            Vector3 p0,
            Vector3 p0m,
            float minEdge,
            float minArea,
            out BodyAnchorBasis basis)
        {
            basis = new BodyAnchorBasis();
            if (candidates == null || candidates.Count < 2)
                return false;

            int pairCount = candidates.Count * candidates.Count;
            for (int attempt = 0; attempt < pairCount; attempt++)
            {
                int n1 = candidates[PositiveMod(StableAnchorHash(index, candidates.Count, attempt, 0), candidates.Count)];
                int n2 = candidates[PositiveMod(StableAnchorHash(index, candidates.Count, attempt, 1), candidates.Count)];
                if (n1 == n2) continue;

                if (TryCreateBodyAnchorBasisFromPair(mesh, index, n1, n2, p0, p0m, minEdge, minArea, out basis))
                    return true;
            }

            return false;
        }

        private static void AddAffectedAnchorCandidates(
            BodyAnchorMesh mesh,
            List<int> source,
            int origin,
            List<int> candidates,
            HashSet<int> seen)
        {
            if (mesh == null || mesh.Valid == null || source == null || candidates == null || seen == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                int candidate = source[i];
                if (candidate == origin) continue;
                if (candidate < 0 || candidate >= mesh.Valid.Length) continue;
                if (!mesh.Valid[candidate]) continue;
                if (seen.Add(candidate))
                    candidates.Add(candidate);
            }
        }

        private static bool TryCreateBodyAnchorBasisFromPair(
            BodyAnchorMesh mesh,
            int index,
            int n1,
            int n2,
            Vector3 p0,
            Vector3 p0m,
            float minEdge,
            float minArea,
            out BodyAnchorBasis basis)
        {
            basis = new BodyAnchorBasis();
            if (mesh == null || mesh.Original == null || mesh.Morphed == null) return false;
            if (index < 0 || index >= mesh.Original.Length || n1 < 0 || n1 >= mesh.Original.Length || n2 < 0 || n2 >= mesh.Original.Length)
                return false;

            Vector3 e1 = mesh.Original[n1] - p0;
            Vector3 e2 = mesh.Original[n2] - p0;
            Vector3 e1m = mesh.Morphed[n1] - p0m;
            Vector3 e2m = mesh.Morphed[n2] - p0m;
            float e1Len = e1.magnitude;
            float e2Len = e2.magnitude;
            float e1mLen = e1m.magnitude;
            float e2mLen = e2m.magnitude;
            if (e1Len < minEdge || e2Len < minEdge || e1mLen < minEdge || e2mLen < minEdge)
                return false;

            Vector3 normal = Vector3.Cross(e1, e2);
            Vector3 normalM = Vector3.Cross(e1m, e2m);
            if (normal.magnitude < minArea || normalM.magnitude < minArea)
                return false;
            float sin = normal.magnitude / Mathf.Max(e1Len * e2Len, 1e-9f);
            float sinM = normalM.magnitude / Mathf.Max(e1mLen * e2mLen, 1e-9f);
            if (sin < 0.12f || sinM < 0.12f)
                return false;

            float stretch1 = e1mLen / Mathf.Max(e1Len, 1e-9f);
            float stretch2 = e2mLen / Mathf.Max(e2Len, 1e-9f);
            if (stretch1 < 0.20f || stretch1 > 5.00f || stretch2 < 0.20f || stretch2 > 5.00f)
                return false;
            if (Vector3.Dot(normal.normalized, normalM.normalized) < -0.20f)
                return false;

            basis.Valid = true;
            basis.Neighbor1 = n1;
            basis.Neighbor2 = n2;
            basis.OriginalInverse = BuildBasisMatrix(e1, e2, normal).inverse;
            return true;
        }

        private static bool TryGetBodySurfaceClothTarget(
            BodyAnchorContext context,
            Vector3 original,
            float clothMultiplier,
            LocalFrame fr,
            out Vector3 target)
        {
            target = original;
            if (context == null || context.Meshes.Count == 0 || context.SurfaceTriangles.Count == 0)
                return false;

            context.ClothQueries++;
            if (!TryFindNearestBodySurfaceTriangle(context, original, out BodySurfaceHit hit))
                return BodyAnchorMiss(context);

            float maxDistance = Mathf.Max(fr.BoneLen * 4.0f, 0.035f);
            if (hit.DistanceSq > maxDistance * maxDistance)
                return BodyAnchorMiss(context);

            if (hit.TriangleIndex < 0 || hit.TriangleIndex >= context.SurfaceTriangles.Count)
                return BodyAnchorMiss(context);

            BodySurfaceTriangle tri = context.SurfaceTriangles[hit.TriangleIndex];
            if (tri.MeshIndex < 0 || tri.MeshIndex >= context.Meshes.Count)
                return BodyAnchorMiss(context);

            BodyAnchorMesh mesh = context.Meshes[tri.MeshIndex];
            if (mesh == null || mesh.Original == null || mesh.Morphed == null)
                return BodyAnchorMiss(context);
            if (!IsValidBodyAnchorVertex(mesh, tri.A)
                || !IsValidBodyAnchorVertex(mesh, tri.B)
                || !IsValidBodyAnchorVertex(mesh, tri.C))
                return BodyAnchorMiss(context);

            float mul = Mathf.Max(0f, clothMultiplier);
            Vector3 a = mesh.Original[tri.A];
            Vector3 b = mesh.Original[tri.B];
            Vector3 c = mesh.Original[tri.C];
            Vector3 am = ScaleAnchorMove(a, mesh.Morphed[tri.A], mul);
            Vector3 bm = ScaleAnchorMove(b, mesh.Morphed[tri.B], mul);
            Vector3 cm = ScaleAnchorMove(c, mesh.Morphed[tri.C], mul);

            Vector3 normalM = Vector3.Cross(bm - am, cm - am);
            float normalMLen = normalM.magnitude;
            if (normalMLen < 1e-9f || !IsFinite(normalM))
                return BodyAnchorMiss(context);
            normalM /= normalMLen;

            if (Vector3.Dot(hit.Normal, normalM) < -0.25f)
                return BodyAnchorMiss(context);

            Vector3 surfaceM =
                am * hit.Barycentric.x +
                bm * hit.Barycentric.y +
                cm * hit.Barycentric.z;
            float signedDistance = Vector3.Dot(original - hit.Closest, hit.Normal);
            target = surfaceM + normalM * signedDistance;
            if (!IsFinite(target))
                return BodyAnchorMiss(context);

            context.ClothHits++;
            return true;
        }

        private static bool TryFindNearestBodySurfaceTriangle(
            BodyAnchorContext context,
            Vector3 point,
            out BodySurfaceHit nearest)
        {
            nearest = new BodySurfaceHit { TriangleIndex = -1 };
            if (context == null || context.SurfaceTriangles.Count == 0)
                return false;

            bool found = false;
            float bestSq = float.MaxValue;

            if (!TryFindNearestBodyAnchorPoint(context, point, true, out BodyAnchorPoint anchor))
                return false;

            return TestBodySurfaceTrianglesForVertex(context, point, anchor, ref found, ref bestSq, ref nearest);
        }

        private static bool TestBodySurfaceTrianglesForVertex(
            BodyAnchorContext context,
            Vector3 point,
            BodyAnchorPoint anchor,
            ref bool found,
            ref float bestSq,
            ref BodySurfaceHit nearest)
        {
            if (context == null || anchor.MeshIndex < 0 || anchor.MeshIndex >= context.Meshes.Count)
                return false;
            BodyAnchorMesh mesh = context.Meshes[anchor.MeshIndex];
            if (mesh?.SurfaceTrianglesByVertex == null)
                return false;
            if (anchor.VertexIndex < 0 || anchor.VertexIndex >= mesh.SurfaceTrianglesByVertex.Length)
                return false;

            List<int> triangles = mesh.SurfaceTrianglesByVertex[anchor.VertexIndex];
            if (triangles == null || triangles.Count == 0)
                return false;

            for (int i = 0; i < triangles.Count; i++)
                TestBodySurfaceTriangle(context, point, triangles[i], ref found, ref bestSq, ref nearest);
            return found;
        }

        private static void TestBodySurfaceTriangle(
            BodyAnchorContext context,
            Vector3 point,
            int triangleIndex,
            ref bool found,
            ref float bestSq,
            ref BodySurfaceHit nearest)
        {
            if (context == null || triangleIndex < 0 || triangleIndex >= context.SurfaceTriangles.Count)
                return;

            BodySurfaceTriangle tri = context.SurfaceTriangles[triangleIndex];
            if (tri.MeshIndex < 0 || tri.MeshIndex >= context.Meshes.Count)
                return;

            BodyAnchorMesh mesh = context.Meshes[tri.MeshIndex];
            if (mesh == null || mesh.Original == null)
                return;
            if (!IsValidBodyAnchorVertex(mesh, tri.A)
                || !IsValidBodyAnchorVertex(mesh, tri.B)
                || !IsValidBodyAnchorVertex(mesh, tri.C))
                return;

            Vector3 a = mesh.Original[tri.A];
            Vector3 b = mesh.Original[tri.B];
            Vector3 c = mesh.Original[tri.C];
            if (!TryClosestPointOnTriangle(point, a, b, c, out Vector3 closest, out Vector3 barycentric))
                return;

            float sq = (point - closest).sqrMagnitude;
            if (sq >= bestSq)
                return;

            Vector3 normal = Vector3.Cross(b - a, c - a);
            float normalLen = normal.magnitude;
            if (normalLen < 1e-9f || !IsFinite(normal))
                return;
            normal /= normalLen;

            found = true;
            bestSq = sq;
            nearest = new BodySurfaceHit
            {
                TriangleIndex = triangleIndex,
                Closest = closest,
                Barycentric = barycentric,
                Normal = normal,
                DistanceSq = sq,
            };
        }

        private static bool TryClosestPointOnTriangle(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out Vector3 closest,
            out Vector3 barycentric)
        {
            closest = a;
            barycentric = new Vector3(1f, 0f, 0f);

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return IsFinite(closest);

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                closest = b;
                barycentric = new Vector3(0f, 1f, 0f);
                return IsFinite(closest);
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float denom = d1 - d3;
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float v = d1 / denom;
                closest = a + ab * v;
                barycentric = new Vector3(1f - v, v, 0f);
                return IsFinite(closest);
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                closest = c;
                barycentric = new Vector3(0f, 0f, 1f);
                return IsFinite(closest);
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float denom = d2 - d6;
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float w = d2 / denom;
                closest = a + ac * w;
                barycentric = new Vector3(1f - w, 0f, w);
                return IsFinite(closest);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float denom = (d4 - d3) + (d5 - d6);
                if (Mathf.Abs(denom) < 1e-9f) return false;
                float w = (d4 - d3) / denom;
                closest = b + (c - b) * w;
                barycentric = new Vector3(0f, 1f - w, w);
                return IsFinite(closest);
            }

            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < 1e-9f)
                return false;

            float inv = 1f / sum;
            float vFace = vb * inv;
            float wFace = vc * inv;
            float uFace = 1f - vFace - wFace;
            closest = a * uFace + b * vFace + c * wFace;
            barycentric = new Vector3(uFace, vFace, wFace);
            return IsFinite(closest) && IsFinite(barycentric);
        }

        private static bool TryGetBodyAnchorClothTarget(
            BodyAnchorContext context,
            Vector3 original,
            float clothMultiplier,
            out Vector3 target)
        {
            target = original;
            if (context == null || context.Points.Count == 0 || context.Meshes.Count == 0)
                return false;
            context.ClothQueries++;
            if (!TryFindNearestBodyAnchorPoint(context, original, out BodyAnchorPoint point))
                return BodyAnchorMiss(context);
            if (point.MeshIndex < 0 || point.MeshIndex >= context.Meshes.Count)
                return BodyAnchorMiss(context);

            BodyAnchorMesh mesh = context.Meshes[point.MeshIndex];
            int p0Index = point.VertexIndex;
            if (mesh == null || mesh.Bases == null || p0Index < 0 || p0Index >= mesh.Bases.Length)
                return BodyAnchorMiss(context);

            BodyAnchorBasis basis = mesh.Bases[p0Index];
            if (!basis.Valid) return BodyAnchorMiss(context);
            if (!IsValidBodyAnchorVertex(mesh, p0Index)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor1)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor2))
                return BodyAnchorMiss(context);

            float mul = Mathf.Max(0f, clothMultiplier);
            Vector3 p0 = mesh.Original[p0Index];
            Vector3 p0m = ScaleAnchorMove(mesh.Original[p0Index], mesh.Morphed[p0Index], mul);
            Vector3 originalOffset = original - p0;
            float distance = originalOffset.magnitude;
            if (distance < 1e-7f)
            {
                target = p0m;
                if (!IsFinite(target)) return BodyAnchorMiss(context);
                context.ClothHits++;
                return true;
            }

            Vector3 local = basis.OriginalInverse.MultiplyVector(originalOffset);
            Vector3 n1m = ScaleAnchorMove(mesh.Original[basis.Neighbor1], mesh.Morphed[basis.Neighbor1], mul);
            Vector3 n2m = ScaleAnchorMove(mesh.Original[basis.Neighbor2], mesh.Morphed[basis.Neighbor2], mul);
            Vector3 e1m = n1m - p0m;
            Vector3 e2m = n2m - p0m;
            Vector3 normalM = Vector3.Cross(e1m, e2m);
            Vector3 transformed = BuildBasisMatrix(e1m, e2m, normalM).MultiplyVector(local);
            if (transformed.sqrMagnitude < 1e-10f || !IsFinite(transformed))
                transformed = originalOffset;
            if (transformed.sqrMagnitude < 1e-10f || !IsFinite(transformed))
                return BodyAnchorMiss(context);

            target = p0m + transformed.normalized * distance;
            if (!IsFinite(target)) return BodyAnchorMiss(context);

            context.ClothHits++;
            return true;
        }

        private static bool TryGetBodyAnchorRigidTarget(
            BodyAnchorContext context,
            Vector3 original,
            bool requireAffected,
            out Vector3 target,
            out Quaternion rotationDelta)
        {
            target = original;
            rotationDelta = Quaternion.identity;
            if (context == null || context.Points.Count == 0 || context.Meshes.Count == 0)
                return false;
            context.ClothQueries++;

            if (!TryFindNearestBodyAnchorPoint(context, original, requireAffected, out BodyAnchorPoint point))
                return BodyAnchorMiss(context);
            if (point.MeshIndex < 0 || point.MeshIndex >= context.Meshes.Count)
                return BodyAnchorMiss(context);

            BodyAnchorMesh mesh = context.Meshes[point.MeshIndex];
            int p0Index = point.VertexIndex;
            if (mesh == null || mesh.Bases == null || p0Index < 0 || p0Index >= mesh.Bases.Length)
                return BodyAnchorMiss(context);
            if (requireAffected && (mesh.Affected == null || !mesh.Affected[p0Index]))
                return BodyAnchorMiss(context);

            BodyAnchorBasis basis = mesh.Bases[p0Index];
            if (!basis.Valid) return BodyAnchorMiss(context);
            if (!IsValidBodyAnchorVertex(mesh, p0Index)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor1)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor2))
                return BodyAnchorMiss(context);

            if (!TryTransformBodyAnchorPoint(mesh, basis, p0Index, original, 1f, out target, out rotationDelta))
                return BodyAnchorMiss(context);

            context.ClothHits++;
            return true;
        }

        private static bool TryTransformBodyAnchorPoint(
            BodyAnchorMesh mesh,
            BodyAnchorBasis basis,
            int p0Index,
            Vector3 original,
            float multiplier,
            out Vector3 target,
            out Quaternion rotationDelta)
        {
            target = original;
            rotationDelta = Quaternion.identity;
            if (mesh == null || mesh.Original == null || mesh.Morphed == null)
                return false;
            if (p0Index < 0 || p0Index >= mesh.Original.Length)
                return false;
            if (!IsValidBodyAnchorVertex(mesh, p0Index)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor1)
                || !IsValidBodyAnchorVertex(mesh, basis.Neighbor2))
                return false;

            float mul = Mathf.Max(0f, multiplier);
            Vector3 p0 = mesh.Original[p0Index];
            Vector3 p0m = ScaleAnchorMove(mesh.Original[p0Index], mesh.Morphed[p0Index], mul);
            Vector3 originalOffset = original - p0;
            float distance = originalOffset.magnitude;

            if (distance < 1e-7f)
            {
                target = p0m;
                TryCreateBodyAnchorRotationDelta(mesh, basis, p0Index, mul, out rotationDelta);
                return IsFinite(target);
            }

            Vector3 local = basis.OriginalInverse.MultiplyVector(originalOffset);
            Vector3 n1m = ScaleAnchorMove(mesh.Original[basis.Neighbor1], mesh.Morphed[basis.Neighbor1], mul);
            Vector3 n2m = ScaleAnchorMove(mesh.Original[basis.Neighbor2], mesh.Morphed[basis.Neighbor2], mul);
            Vector3 e1m = n1m - p0m;
            Vector3 e2m = n2m - p0m;
            Vector3 normalM = Vector3.Cross(e1m, e2m);
            Vector3 transformed = BuildBasisMatrix(e1m, e2m, normalM).MultiplyVector(local);
            if (transformed.sqrMagnitude < 1e-10f || !IsFinite(transformed))
                transformed = originalOffset;
            if (transformed.sqrMagnitude < 1e-10f || !IsFinite(transformed))
                return false;

            target = p0m + transformed.normalized * distance;
            if (!IsFinite(target)) return false;
            TryCreateBodyAnchorRotationDelta(mesh, basis, p0Index, mul, out rotationDelta);
            return true;
        }

        private static bool TryCreateBodyAnchorRotationDelta(
            BodyAnchorMesh mesh,
            BodyAnchorBasis basis,
            int p0Index,
            float multiplier,
            out Quaternion rotationDelta)
        {
            rotationDelta = Quaternion.identity;
            try
            {
                Vector3 p0 = mesh.Original[p0Index];
                Vector3 p0m = ScaleAnchorMove(mesh.Original[p0Index], mesh.Morphed[p0Index], multiplier);
                Vector3 e1 = mesh.Original[basis.Neighbor1] - p0;
                Vector3 e2 = mesh.Original[basis.Neighbor2] - p0;
                Vector3 e1m = ScaleAnchorMove(mesh.Original[basis.Neighbor1], mesh.Morphed[basis.Neighbor1], multiplier) - p0m;
                Vector3 e2m = ScaleAnchorMove(mesh.Original[basis.Neighbor2], mesh.Morphed[basis.Neighbor2], multiplier) - p0m;

                if (!TryBuildAnchorRotation(e1, e2, out Quaternion oldRot)) return false;
                if (!TryBuildAnchorRotation(e1m, e2m, out Quaternion newRot)) return false;
                rotationDelta = newRot * Quaternion.Inverse(oldRot);
                if (!IsFinite(rotationDelta))
                {
                    rotationDelta = Quaternion.identity;
                    return false;
                }
                return true;
            }
            catch
            {
                rotationDelta = Quaternion.identity;
                return false;
            }
        }

        private static bool TryBuildAnchorRotation(Vector3 e1, Vector3 e2, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            Vector3 right = e1;
            if (right.sqrMagnitude < 1e-10f) return false;
            right.Normalize();

            Vector3 normal = Vector3.Cross(e1, e2);
            if (normal.sqrMagnitude < 1e-10f) return false;
            normal.Normalize();

            Vector3 up = Vector3.Cross(normal, right);
            if (up.sqrMagnitude < 1e-10f) return false;
            up.Normalize();

            rotation = Quaternion.LookRotation(normal, up);
            return IsFinite(rotation);
        }

        private static bool TryGetBodyAnchorLinearTarget(
            BodyAnchorContext context,
            Vector3 original,
            bool requireAffected,
            out Vector3 target)
        {
            target = original;
            if (context == null || context.Points.Count == 0 || context.Meshes.Count == 0)
                return false;
            if (!TryFindNearestBodyAnchorPoint(context, original, requireAffected, out BodyAnchorPoint point))
                return false;
            if (point.MeshIndex < 0 || point.MeshIndex >= context.Meshes.Count)
                return false;

            BodyAnchorMesh mesh = context.Meshes[point.MeshIndex];
            int index = point.VertexIndex;
            if (mesh == null || mesh.Original == null || mesh.Morphed == null || index < 0 || index >= mesh.Original.Length)
                return false;
            if (requireAffected && (mesh.Affected == null || !mesh.Affected[index]))
                return false;

            target = original + (mesh.Morphed[index] - mesh.Original[index]);
            return IsFinite(target);
        }

        private static bool AcceptBodyAnchorClothTarget(
            BodyAnchorContext context,
            Vector3 original,
            Vector3 baseTarget,
            Vector3 anchorTarget,
            LocalFrame fr)
        {
            if (!IsFinite(baseTarget) || !IsFinite(anchorTarget))
            {
                if (context != null) context.ClothRejected++;
                return false;
            }

            var p = BellyDeformSettings.Vtx;
            float boneLen = Mathf.Max(fr.BoneLen, 1e-5f);
            float threshold = Mathf.Max(0.35f, p.ClothDistortThreshold);
            Vector3 baseDisp = baseTarget - original;
            Vector3 anchorDisp = anchorTarget - original;
            float baseMag = baseDisp.magnitude;
            float anchorMag = anchorDisp.magnitude;
            float delta = (anchorTarget - baseTarget).magnitude;

            float maxDelta = Mathf.Max(baseMag * 0.90f, boneLen * threshold * 0.65f);
            if (delta > maxDelta)
            {
                if (context != null) context.ClothRejected++;
                return false;
            }

            float maxMag = Mathf.Max(baseMag * 2.25f, baseMag + boneLen * threshold);
            if (anchorMag > maxMag)
            {
                if (context != null) context.ClothRejected++;
                return false;
            }

            if (baseMag > boneLen * 0.05f && anchorMag > boneLen * 0.05f)
            {
                float dir = Vector3.Dot(baseDisp / baseMag, anchorDisp / anchorMag);
                if (dir < -0.25f)
                {
                    if (context != null) context.ClothRejected++;
                    return false;
                }
            }

            return true;
        }

        private static bool BodyAnchorMiss(BodyAnchorContext context)
        {
            if (context != null) context.ClothMisses++;
            return false;
        }

        private static Vector3 ScaleAnchorMove(Vector3 original, Vector3 morphed, float multiplier)
        {
            return original + (morphed - original) * multiplier;
        }

        private static bool IsValidBodyAnchorVertex(BodyAnchorMesh mesh, int index)
        {
            return mesh != null
                && mesh.Valid != null
                && index >= 0
                && index < mesh.Valid.Length
                && mesh.Valid[index];
        }

        private static bool TryFindNearestBodyAnchorPoint(BodyAnchorContext context, Vector3 point, out BodyAnchorPoint nearest)
        {
            return TryFindNearestBodyAnchorPoint(context, point, false, out nearest);
        }

        private static bool TryFindNearestBodyAnchorPoint(BodyAnchorContext context, Vector3 point, bool requireAffected, out BodyAnchorPoint nearest)
        {
            nearest = new BodyAnchorPoint();
            if (context == null || context.Points.Count == 0)
                return false;

            BodyAnchorBucketCoords(point, context.CellSize, out int bx, out int by, out int bz);
            bool found = false;
            float bestSq = float.MaxValue;

            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        int key = BodyAnchorBucketKey(bx + dx, by + dy, bz + dz);
                        if (!context.Buckets.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                            TestBodyAnchorPoint(context, point, list[i], requireAffected, ref found, ref bestSq, ref nearest);
                    }

            if (found) return true;

            for (int i = 0; i < context.Points.Count; i++)
                TestBodyAnchorPoint(context, point, i, requireAffected, ref found, ref bestSq, ref nearest);
            return found;
        }

        private static void TestBodyAnchorPoint(
            BodyAnchorContext context,
            Vector3 point,
            int pointIndex,
            bool requireAffected,
            ref bool found,
            ref float bestSq,
            ref BodyAnchorPoint nearest)
        {
            if (context == null || pointIndex < 0 || pointIndex >= context.Points.Count) return;
            BodyAnchorPoint candidate = context.Points[pointIndex];
            if (requireAffected)
            {
                if (candidate.MeshIndex < 0 || candidate.MeshIndex >= context.Meshes.Count) return;
                BodyAnchorMesh mesh = context.Meshes[candidate.MeshIndex];
                if (mesh?.Affected == null || candidate.VertexIndex < 0 || candidate.VertexIndex >= mesh.Affected.Length) return;
                if (!mesh.Affected[candidate.VertexIndex]) return;
            }

            float sq = (candidate.Original - point).sqrMagnitude;
            if (sq >= bestSq) return;

            found = true;
            bestSq = sq;
            nearest = candidate;
        }

        private static List<int>[] BuildMeshNeighbors(Mesh mesh, int count)
        {
            if (mesh == null || count <= 0) return null;
            try
            {
                var neighbors = new List<int>[count];
                for (int i = 0; i < count; i++)
                    neighbors[i] = new List<int>(8);

                int[] triangles = mesh.triangles;
                if (triangles == null || triangles.Length < 3)
                    return neighbors;

                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    AddNeighborPair(neighbors, count, a, b);
                    AddNeighborPair(neighbors, count, b, c);
                    AddNeighborPair(neighbors, count, c, a);
                }

                return neighbors;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] BuildMeshNeighbors: " + e.Message);
                return null;
            }
        }

        private static void AddNeighborPair(List<int>[] neighbors, int count, int a, int b)
        {
            if (a < 0 || b < 0 || a >= count || b >= count || a == b) return;
            if (!neighbors[a].Contains(b)) neighbors[a].Add(b);
            if (!neighbors[b].Contains(a)) neighbors[b].Add(a);
        }

        private static Matrix4x4 BuildBasisMatrix(Vector3 e1, Vector3 e2, Vector3 normal)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(e1.x, e1.y, e1.z, 0f));
            matrix.SetColumn(1, new Vector4(e2.x, e2.y, e2.z, 0f));
            matrix.SetColumn(2, new Vector4(normal.x, normal.y, normal.z, 0f));
            matrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return matrix;
        }

        private static int StableAnchorHash(int index, int count, int attempt, int salt)
        {
            unchecked
            {
                int hash = 216613626;
                hash = (hash * 16777619) ^ index;
                hash = (hash * 16777619) ^ count;
                hash = (hash * 16777619) ^ attempt;
                hash = (hash * 16777619) ^ salt;
                return hash;
            }
        }

        private static int PositiveMod(int value, int mod)
        {
            if (mod <= 0) return 0;
            int result = value % mod;
            return result < 0 ? result + mod : result;
        }

        private static void BodyAnchorBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = Mathf.Max(cellSize, 0.0001f);
            x = Mathf.FloorToInt(point.x / cell);
            y = Mathf.FloorToInt(point.y / cell);
            z = Mathf.FloorToInt(point.z / cell);
        }

        private static int BodyAnchorBucketKey(int x, int y, int z)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }

        private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        private static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        private static int RepairClothDistortion(
            Vector3[] original,
            Vector3[] morphed,
            bool[] moved,
            LocalFrame fr,
            List<int>[] topologyNeighbors)
        {
            try
            {
                var p = BellyDeformSettings.Vtx;
                float detectThreshold = p.ClothDistortThreshold;
                float normalNeighborDiff = p.ClothDistortNeighborDiff;
                if (detectThreshold <= 0f || normalNeighborDiff < 0f) return 0;
                if (original == null || morphed == null || moved == null) return 0;
                int count = original.Length;
                if (morphed.Length != count || moved.Length != count) return 0;
                if (topologyNeighbors == null || topologyNeighbors.Length != count) return 0;

                float boneLen = Mathf.Max(fr.BoneLen, 1e-5f);
                float minMoveSq = boneLen * boneLen * 1e-8f;
                var movedIndices = new List<int>();

                for (int i = 0; i < count; i++)
                {
                    if (!moved[i]) continue;
                    Vector3 disp = morphed[i] - original[i];
                    if (disp.sqrMagnitude <= minMoveSq) continue;

                    movedIndices.Add(i);
                }

                if (movedIndices.Count < 3) return 0;
                float radius = Mathf.Max(boneLen * 1.50f, 0.012f);
                float radiusSq = radius * radius;
                float cell = Mathf.Max(radius, 0.001f);
                var buckets = BuildSpatialBuckets(original, movedIndices, cell);
                if (buckets.Count == 0) return 0;

                var replacement = new Vector3[count];
                var shouldReplace = new bool[count];
                var neighbors = new List<int>(48);
                int fixedCount = 0;
                int checkedCount = 0;
                int noNeighborCount = 0;
                int noSampleCount = 0;
                float maxDiffNorm = 0f;
                float maxEdgeDiffNorm = 0f;

                for (int m = 0; m < movedIndices.Count; m++)
                {
                    int i = movedIndices[m];
                    List<int> topo = topologyNeighbors[i];
                    if (topo == null || topo.Count == 0)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 dispI = morphed[i] - original[i];
                    Vector3 topoSum = Vector3.zero;
                    int topoSamples = 0;
                    int tornEdges = 0;
                    float localMaxEdgeDiffNorm = 0f;

                    for (int t = 0; t < topo.Count; t++)
                    {
                        int j = topo[t];
                        if (j == i || j < 0 || j >= count || !moved[j]) continue;
                        Vector3 dispJ = morphed[j] - original[j];
                        if (dispJ.sqrMagnitude <= minMoveSq) continue;

                        float edgeDiffNorm = (dispI - dispJ).magnitude / boneLen;
                        if (edgeDiffNorm > localMaxEdgeDiffNorm)
                            localMaxEdgeDiffNorm = edgeDiffNorm;
                        if (edgeDiffNorm >= detectThreshold)
                            tornEdges++;

                        topoSum += dispJ;
                        topoSamples++;
                    }

                    if (localMaxEdgeDiffNorm > maxEdgeDiffNorm)
                        maxEdgeDiffNorm = localMaxEdgeDiffNorm;
                    if (topoSamples < 2 || tornEdges == 0)
                    {
                        if (topoSamples < 2) noNeighborCount++;
                        continue;
                    }

                    neighbors.Clear();
                    CollectSpatialNeighbors(original, buckets, cell, radiusSq, i, neighbors);
                    if (neighbors.Count == 0)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 localSum = topoSum;
                    int localSamples = topoSamples;
                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int j = neighbors[n];
                        if (j == i || !moved[j]) continue;

                        Vector3 dispJ = morphed[j] - original[j];
                        if (dispJ.sqrMagnitude <= minMoveSq) continue;
                        localSum += dispJ;
                        localSamples++;
                    }

                    if (localSamples < 2)
                    {
                        noNeighborCount++;
                        continue;
                    }

                    Vector3 localAvg = localSum / localSamples;
                    float diffNorm = (dispI - localAvg).magnitude / boneLen;
                    if (diffNorm > maxDiffNorm)
                        maxDiffNorm = diffNorm;
                    checkedCount++;

                    if (diffNorm < detectThreshold) continue;

                    Vector3 sum = Vector3.zero;
                    int samples = 0;
                    for (int t = 0; t < topo.Count; t++)
                    {
                        int k = topo[t];
                        if (k == i || k < 0 || k >= count || !moved[k]) continue;

                        Vector3 kDisp = morphed[k] - original[k];
                        if (kDisp.sqrMagnitude <= minMoveSq) continue;

                        float sampleDiff = (kDisp - localAvg).magnitude / boneLen;
                        if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                        {
                            sum += kDisp;
                            samples++;
                        }
                    }

                    if (samples == 0)
                    {
                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            int k = neighbors[n];
                            if (k == i || !moved[k]) continue;

                            Vector3 kDisp = morphed[k] - original[k];
                            if (kDisp.sqrMagnitude <= minMoveSq) continue;

                            float sampleDiff = (kDisp - localAvg).magnitude / boneLen;
                            if (sampleDiff <= normalNeighborDiff || sampleDiff < diffNorm * 0.50f)
                            {
                                sum += kDisp;
                                samples++;
                            }
                        }
                    }

                    Vector3 replacementDisp;
                    if (samples > 0)
                    {
                        replacementDisp = sum / samples;
                    }
                    else
                    {
                        noSampleCount++;
                        if (tornEdges < 2 || localMaxEdgeDiffNorm < detectThreshold * 2f)
                            continue;
                        replacementDisp = topoSum / topoSamples;
                    }

                    replacement[i] = original[i] + replacementDisp;
                    shouldReplace[i] = true;
                    fixedCount++;
                }

                if (checkedCount > 0 && (fixedCount > 0 || maxDiffNorm >= detectThreshold * 0.75f || maxEdgeDiffNorm >= detectThreshold))
                    RuntimeLogInfo($"[VtxMorph] ClothDistortFix: fixed={fixedCount}/{movedIndices.Count} checked={checkedCount} noNbr={noNeighborCount} noSample={noSampleCount} maxDiff={maxDiffNorm:F2} maxEdge={maxEdgeDiffNorm:F2} thr={detectThreshold:F2} near={normalNeighborDiff:F2}");

                if (fixedCount == 0) return 0;
                for (int i = 0; i < count; i++)
                    if (shouldReplace[i])
                        morphed[i] = replacement[i];
                return fixedCount;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] ClothDistortFix: " + e.Message);
                return 0;
            }
        }

        private static Dictionary<int, List<int>> BuildSpatialBuckets(Vector3[] points, List<int> indices, float cellSize)
        {
            var buckets = new Dictionary<int, List<int>>();
            if (points == null || indices == null) return buckets;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= points.Length) continue;
                SpatialBucketCoords(points[index], cellSize, out int x, out int y, out int z);
                int key = SpatialBucketKey(x, y, z);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(8);
                    buckets[key] = list;
                }
                list.Add(index);
            }
            return buckets;
        }

        private static void CollectSpatialNeighbors(
            Vector3[] points,
            Dictionary<int, List<int>> buckets,
            float cellSize,
            float radiusSq,
            int centerIndex,
            List<int> result)
        {
            if (points == null || buckets == null || result == null) return;
            if (centerIndex < 0 || centerIndex >= points.Length) return;

            Vector3 center = points[centerIndex];
            SpatialBucketCoords(center, cellSize, out int bx, out int by, out int bz);
            int range = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(radiusSq) / Mathf.Max(cellSize, 1e-5f)));

            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                    for (int dz = -range; dz <= range; dz++)
                    {
                        int key = SpatialBucketKey(bx + dx, by + dy, bz + dz);
                        if (!buckets.TryGetValue(key, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            int candidate = list[i];
                            if (candidate == centerIndex) continue;
                            if (candidate < 0 || candidate >= points.Length) continue;
                            if ((points[candidate] - center).sqrMagnitude <= radiusSq)
                                result.Add(candidate);
                        }
                    }
        }

        private static void SpatialBucketCoords(Vector3 point, float cellSize, out int x, out int y, out int z)
        {
            float cell = Mathf.Max(cellSize, 0.0001f);
            x = Mathf.FloorToInt(point.x / cell);
            y = Mathf.FloorToInt(point.y / cell);
            z = Mathf.FloorToInt(point.z / cell);
        }

        private static int SpatialBucketKey(int x, int y, int z)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }

        // ── Belly mask from bone weights (PP vertex-selection mechanism) ──

        private static bool[] ComputeBellyMask(
            Mesh mesh, int vertexCount,
            HashSet<int> bellyBoneSet, HashSet<int> legBoneSet)
        {
            if (bellyBoneSet == null || bellyBoneSet.Count == 0)
                return null;
            try
            {
                var bw = mesh.boneWeights;
                if (bw == null || bw.Length != vertexCount)
                {
                    Log.LogWarning($"[VtxMorph] BellyMask: boneWeights length mismatch " +
                                   $"({bw?.Length ?? -1} vs {vertexCount})");
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

                RuntimeLogInfo($"[VtxMorph] BellyMask: {pass}/{vertexCount} vertices pass bone-weight filter");

                if (pass < vertexCount * 0.05f)
                {
                    Log.LogWarning($"[VtxMorph] BellyMask: too few vertices ({pass}) — disabling body filter");
                    return null;
                }
                return mask;
            }
            catch (Exception e)
            {
                Log.LogWarning("[VtxMorph] BellyMask: " + e.Message);
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
                    RuntimeLogInfo("[VtxMorph] BreastWeights: no breast bones found — guard disabled");
                    return null;
                }
                RuntimeLogInfo($"[VtxMorph] BreastWeights: found {breastSet.Count} breast bone(s)");

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
                RuntimeLogInfo($"[VtxMorph] BreastWeights: {found}/{vertexCount} vertices have breast-bone weight");
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

        private static long StateKey(int charaId, IntPtr humanPtr)
        {
            unchecked
            {
                long ptr = humanPtr.ToInt64();
                return ptr != 0 ? (ptr ^ ((long)charaId << 32)) : charaId;
            }
        }

        private static List<long> KeysForChara(int charaId)
        {
            var keys = new List<long>();
            foreach (var pair in _state)
                if (pair.Value != null && pair.Value.CharaId == charaId)
                    keys.Add(pair.Key);
            return keys;
        }

        private static bool RateClose(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b)) return false;
            return Math.Abs(a - b) <= 0.001f;
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

        private static string PathOf(Transform t)
        {
            try
            {
                if (t == null) return "(null)";
                var parts = new List<string>();
                for (var c = t; c != null && parts.Count < 18; c = c.parent)
                    parts.Add(c.name);
                parts.Reverse();
                return string.Join("/", parts);
            }
            catch { return "(path-error)"; }
        }
    }
}

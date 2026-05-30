using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Logging;
using Character;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Networking;

namespace SVSPregnancy
{
    internal static class MeshSpyFull
    {
        private static bool SpyLogEnabled => PregnancyPlugin.MeshSpyDebugEnabled;
        private const int MaxTotalLogs = 30000;
        private const int MaxDiskProbeBytes = 2 * 1024 * 1024;
        private const int MaxDiskMeshStringScanBytes = 16 * 1024 * 1024;
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.MeshSpy");
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> BundleSources = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> CreateRequestSources = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> AssetRequestSources = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> MeshSources = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> BinarySources = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> StreamSources = new Dictionary<string, string>();
        private static readonly HashSet<string> ProbedMeshes = new HashSet<string>();
        private static readonly HashSet<string> ProbedBundles = new HashSet<string>();
        private static readonly HashSet<string> ProbedDiskMeshRoutes = new HashSet<string>();
        private static readonly HashSet<string> ProbedWeightStats = new HashSet<string>();
        private static readonly HashSet<string> SlotComponentSnapshots = new HashSet<string>();
        private static MethodInfo _acquireReadOnlyMeshDataMethod;
        private static bool _acquireReadOnlyMeshDataSearched;
        private static bool _installed;
        private static bool _limitLogged;
        private static int _totalLogs;
        [ThreadStatic] private static bool _suppressAssetLoadSpy;

        public static void Install(Harmony harmony)
        {
            if (_installed || harmony == null) return;
            _installed = true;

            int patched = 0;
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAsset", postfix: nameof(AssetBundleLoadAssetPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAssetAsync", postfix: nameof(AssetBundleLoadAssetAsyncPostfix));
            patched += PatchMethod(harmony, typeof(AssetBundleRequest), "get_asset", postfix: nameof(AssetBundleRequestAssetPostfix));

            if (!SpyLogEnabled)
            {
                LogPlain($"[MeshSpy] install patched={patched} mode=loader-readable-only");
                return;
            }

            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromFile", postfix: nameof(AssetBundleLoadFromFilePostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromFileAsync", postfix: nameof(AssetBundleLoadFromFileAsyncPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromMemory", postfix: nameof(AssetBundleLoadFromMemoryPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromMemoryAsync", postfix: nameof(AssetBundleCreateRequestResultPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromStream", postfix: nameof(AssetBundleLoadFromStreamPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadFromStreamAsync", postfix: nameof(AssetBundleCreateRequestResultPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAllAssets", postfix: nameof(AssetBundleLoadAllAssetsPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAllAssetsAsync", postfix: nameof(AssetBundleLoadAllAssetsAsyncPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAssetWithSubAssets", postfix: nameof(AssetBundleLoadAssetWithSubAssetsPostfix));
            patched += PatchAllMethods(harmony, typeof(AssetBundle), "LoadAssetWithSubAssetsAsync", postfix: nameof(AssetBundleLoadAssetWithSubAssetsAsyncPostfix));
            patched += PatchMethod(harmony, typeof(AssetBundleRequest), "get_allAssets", postfix: nameof(AssetBundleRequestAllAssetsPostfix));
            patched += PatchMethod(harmony, typeof(AssetBundleCreateRequest), "get_assetBundle", postfix: nameof(AssetBundleCreateRequestAssetBundlePostfix));
            patched += PatchMethod(harmony, typeof(SkinnedMeshRenderer), "set_sharedMesh", postfix: nameof(SMRSharedMeshPostfix));
            patched += PatchMethod(harmony, typeof(SkinnedMeshRenderer), "set_bones", postfix: nameof(SMRBonesPostfix));
            patched += PatchMethod(harmony, typeof(SkinnedMeshRenderer), "set_rootBone", postfix: nameof(SMRRootBonePostfix));
            patched += PatchMethod(harmony, typeof(MeshFilter), "set_sharedMesh", postfix: nameof(MeshFilterSharedMeshPostfix));
            patched += PatchMethod(harmony, typeof(Transform), "SetParent", args: new[] { typeof(Transform) }, postfix: nameof(TransformParentPostfix));
            patched += PatchMethod(harmony, typeof(Transform), "SetParent", args: new[] { typeof(Transform), typeof(bool) }, postfix: nameof(TransformMutationPostfix));
            patched += PatchMethod(harmony, typeof(Transform), "set_parent", args: new[] { typeof(Transform) }, postfix: nameof(TransformMutationPostfix));

            LogPlain($"[MeshSpy] install patched={patched} mode=loader-readable-and-structural-invalidate maxLogs={MaxTotalLogs}");
        }

        public static void SnapshotHuman(Human human, int charaId, float rate, string tag)
        {
            if (!SpyLogEnabled) return;
            try
            {
                if (human == null || human.gameObject == null) return;
                string rootPath = PathOf(human.transform);
                string key = $"snapshot|{tag}|{charaId}|{human.hiPoly}|{rootPath}|{rate:F3}";
                if (!ShouldLog(key, 1)) return;

                var smrs = human.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                LogPlain($"[MeshSpy] Snapshot tag={tag} charaId={charaId} rate={rate:F3} hiPoly={human.hiPoly} root=\"{rootPath}\" smrs={smrs?.Length ?? 0}");
                if (smrs == null) return;

                int shown = 0;
                foreach (var smr in smrs)
                {
                    if (smr == null || smr.sharedMesh == null) continue;
                    if (!LooksInteresting(smr.name) && !LooksInteresting(smr.sharedMesh.name)) continue;
                    LogPlain("[MeshSpy] SnapshotSMR " + DescribeSMR(smr));
                    ProbeSMRMeshAccess($"Snapshot:{tag}:{charaId}", smr);
                    LogSMRWeightStats($"Snapshot:{tag}:{charaId}", smr, smr.sharedMesh);
                    if (++shown >= 48)
                    {
                        LogPlain("[MeshSpy] SnapshotSMR truncated");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                LogLimited("snapshot-error", "[MeshSpy] Snapshot failed: " + e.Message, 3);
            }
        }

        private static int PatchMethod(Harmony harmony, Type type, string name, Type[] args = null, string prefix = null, string postfix = null)
        {
            try
            {
                var method = args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
                if (method == null) return 0;
                harmony.Patch(
                    method,
                    prefix == null ? null : new HarmonyMethod(typeof(MeshSpyFull), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(MeshSpyFull), postfix));
                return 1;
            }
            catch (Exception e)
            {
                LogLimited($"patch-fail|{type?.Name}|{name}", $"[MeshSpy] patch failed {type?.FullName}.{name}: {e.Message}", 1);
                return 0;
            }
        }

        private static int PatchAllMethods(Harmony harmony, Type type, string name, string prefix = null, string postfix = null)
        {
            int patched = 0;
            try
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method == null || method.Name != name || method.ContainsGenericParameters) continue;
                    try
                    {
                        harmony.Patch(
                            method,
                            prefix == null ? null : new HarmonyMethod(typeof(MeshSpyFull), prefix),
                            postfix == null ? null : new HarmonyMethod(typeof(MeshSpyFull), postfix));
                        patched++;
                    }
                    catch (Exception e)
                    {
                        LogLimited($"patch-fail|{type?.Name}|{name}|{patched}", $"[MeshSpy] patch failed {type?.FullName}.{name}: {e.Message}", 1);
                    }
                }
            }
            catch (Exception e)
            {
                LogLimited($"patch-all-fail|{type?.Name}|{name}", $"[MeshSpy] patch-all failed {type?.FullName}.{name}: {e.Message}", 1);
            }
            return patched;
        }

        private static int PatchObjectInstantiate(Harmony harmony)
        {
            int patched = 0;
            try
            {
                foreach (var method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != "Instantiate" || method.ContainsGenericParameters) continue;
                    var ps = method.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(UnityEngine.Object)) continue;
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(MeshSpyFull), nameof(ObjectInstantiatePostfix)));
                        patched++;
                    }
                    catch (Exception e)
                    {
                        LogLimited($"patch-fail|Object.Instantiate|{patched}", "[MeshSpy] patch failed Object.Instantiate: " + e.Message, 1);
                    }
                }
            }
            catch (Exception e)
            {
                LogLimited("patch-fail|Object.Instantiate", "[MeshSpy] patch failed Object.Instantiate: " + e.Message, 1);
            }
            return patched;
        }

        private static int PatchLikelyCustomShapeMethods(Harmony harmony)
        {
            const int maxPatches = 220;
            int patched = 0;
            try
            {
                var targetAssembly = typeof(Human).Assembly;
                foreach (var type in SafeGetTypes(targetAssembly))
                {
                    if (type == null || !IsLikelyCustomShapeType(type)) continue;
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    }
                    catch { continue; }

                    foreach (var method in methods)
                    {
                        if (patched >= maxPatches) break;
                        if (!IsLikelyCustomShapeMethod(method)) continue;
                        try
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MeshSpyFull), nameof(CustomShapeMethodPostfix)));
                            patched++;
                        }
                        catch (Exception e)
                        {
                            LogLimited($"patch-custom-fail|{type.FullName}|{method.Name}", $"[MeshSpy] patch failed CustomShape {type.FullName}.{method.Name}: {e.Message}", 1);
                        }
                    }
                    if (patched >= maxPatches) break;
                }
            }
            catch (Exception e)
            {
                LogLimited("patch-custom-fail", "[MeshSpy] patch CustomShape scan failed: " + e.Message, 1);
            }

            LogLimited("patch-custom-count", $"[MeshSpy] CustomShape spy patched={patched}", 1);
            return patched;
        }

        private static void LogLikelyAssetHookCandidates()
        {
            try
            {
                int shown = 0;
                var targetAssembly = typeof(Human).Assembly;
                foreach (var type in SafeGetTypes(targetAssembly))
                {
                    if (type == null || !IsLikelyAssetPipelineType(type)) continue;
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    }
                    catch { continue; }

                    foreach (var method in methods)
                    {
                        if (!IsLikelyAssetPipelineMethod(method)) continue;
                        LogLimited(
                            $"asset-candidate|{type.FullName}|{method.Name}|{ParamSignature(method)}",
                            $"[MeshSpy] AssetHookCandidate {type.FullName}.{method.Name}({ParamSignature(method)}) returns={SafeTypeName(method.ReturnType)}",
                            1);
                        if (++shown >= 80)
                        {
                            LogLimited("asset-candidate-truncated", "[MeshSpy] AssetHookCandidate truncated at 80", 1);
                            return;
                        }
                    }
                }
                LogLimited("asset-candidate-count", $"[MeshSpy] AssetHookCandidate totalShown={shown}", 1);
            }
            catch (Exception e)
            {
                LogLimited("asset-candidate-error", "[MeshSpy] AssetHookCandidate scan failed: " + e.Message, 1);
            }
        }

        private static void SMRSharedMeshPostfix(SkinnedMeshRenderer __instance, Mesh value)
        {
            try
            {
                if (__instance == null || value == null) return;
                LogInterestingSMR("set_sharedMesh", __instance, value, 4);
                // InvalidateAll removed: do not trigger deform during mesh loading
            }
            catch { }
        }

        private static void MeshFilterSharedMeshPostfix(MeshFilter __instance, Mesh value)
        {
            try
            {
                if (__instance == null || value == null) return;
                string key = $"mf-shared|{PathOf(__instance.transform)}|{value.name}|{value.vertexCount}";
                LogLimited(key, $"[MeshSpy] MeshFilter.set_sharedMesh name=\"{__instance.name}\" mesh=\"{MeshInfo(value)}\" path=\"{PathOf(__instance.transform)}\"", 2);
                // InvalidateAll removed: do not trigger deform during mesh loading
            }
            catch { }
        }

        private static void SMRBonesPostfix(SkinnedMeshRenderer __instance)
        {
            try
            {
                if (__instance == null || __instance.sharedMesh == null) return;
                LogInterestingSMR("set_bones", __instance, __instance.sharedMesh, 3);
                // InvalidateAll removed: do not trigger deform during mesh loading
            }
            catch { }
        }

        private static void SMRRootBonePostfix(SkinnedMeshRenderer __instance)
        {
            try
            {
                if (__instance == null || __instance.sharedMesh == null) return;
                LogInterestingSMR("set_rootBone", __instance, __instance.sharedMesh, 3);
                // InvalidateAll removed: do not trigger deform during mesh loading
            }
            catch { }
        }

        private static void SMRLocalBoundsPostfix(SkinnedMeshRenderer __instance)
        {
            try
            {
                if (__instance == null || __instance.sharedMesh == null) return;
                LogInterestingSMR("set_localBounds", __instance, __instance.sharedMesh, 2);
            }
            catch { }
        }

        private static void RendererEnabledPostfix(Renderer __instance, bool value)
        {
            try
            {
                var smr = __instance?.TryCast<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null) return;
                LogInterestingSMR($"renderer_enabled={value}", smr, smr.sharedMesh, 2);
            }
            catch { }
        }

        private static void ObjectNamePostfix(UnityEngine.Object __instance, string value)
        {
            try
            {
                LogObject("Object.set_name", $"name=\"{value}\"", __instance, 2);
            }
            catch { }
        }

        private static void TransformParentPostfix(Transform __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;
                // InvalidateAll removed: do not trigger deform during loading
            }
            catch { }
        }

        private static void TransformMutationPostfix(Transform __instance, MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;
                // InvalidateAll removed: do not trigger deform during loading
            }
            catch { }
        }

        private static void LogTransformMutation(string source, Transform t, object[] args)
        {
            try
            {
                if (!LooksSlotClothOrAccessoryTransform(t)) return;
                string path = PathOf(t);
                string parentPath = PathOf(t.parent);
                string key = $"transform-mut|{source}|{path}|{ArgKey(args)}";
                if (!ShouldLog(key, 6)) return;

                string argPart = args == null || args.Length == 0 ? "" : " args=" + ArgSummary(args, 4);
                LogPlain(
                    $"[MeshSpy] TransformMutation source=\"{source}\" name=\"{t.name}\" path=\"{path}\" parentPath=\"{parentPath}\"" +
                    $" localPos={Fmt(t.localPosition)} localEuler={Fmt(t.localEulerAngles)} localRot={Fmt(t.localRotation)} localScale={Fmt(t.localScale)}" +
                    $" worldPos={Fmt(t.position)} worldEuler={Fmt(t.eulerAngles)} worldRot={Fmt(t.rotation)} active={t.gameObject.activeSelf}/{t.gameObject.activeInHierarchy}{argPart}");

                LogGameObject(source, $"parentPath=\"{parentPath}\"", t.gameObject, 2);
                LogSlotComponentSnapshot(source, t);
            }
            catch (Exception e)
            {
                LogLimited("transform-mut-error", "[MeshSpy] TransformMutation error: " + Short(e.Message, 120), 6);
            }
        }

        private static bool LooksSlotClothOrAccessoryTransform(Transform t)
        {
            try
            {
                for (var c = t; c != null; c = c.parent)
                {
                    if (LooksSlotClothOrAccessoryName(c.name)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool LooksSlotClothOrAccessoryName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string s = name.ToLowerInvariant();
            return s.Contains("ca_slot") ||
                   s == "nc" ||
                   s.Contains("n_move") ||
                   s.Contains("n_chousei") ||
                   s.Contains("o_acs") ||
                   s.Contains("n_acs") ||
                   s.Contains("_acs") ||
                   s.Contains("acs_") ||
                   s.Contains("accessory") ||
                   s.Contains("accessories") ||
                   s.Contains("accessary") ||
                   s.Contains("belt") ||
                   s.Contains("navel") ||
                   s.Contains("pierce") ||
                   s.Contains("ribbon") ||
                   s.Contains("chain") ||
                   s.Contains("charm") ||
                   s.Contains("o_top") ||
                   s.Contains("o_bot") ||
                   s.Contains("o_bra") ||
                   s.Contains("o_shorts") ||
                   s.Contains("o_panst") ||
                   s.Contains("o_socks") ||
                   s.Contains("o_sock") ||
                   s.Contains("o_shocks") ||
                   s.Contains("n_top") ||
                   s.Contains("n_bot") ||
                   s.Contains("skirt") ||
                   s.Contains("onep") ||
                   s.Contains("onepiece") ||
                   s.Contains("bra") ||
                   s.Contains("shorts") ||
                   s.Contains("panst") ||
                   s.Contains("socks") ||
                   s.Contains("sock") ||
                   s.Contains("shocks") ||
                   s.Contains("tights") ||
                   s.Contains("stocking") ||
                   s.Contains("garter") ||
                   s.Contains("swim") ||
                   s.Contains("mizugi") ||
                   s.Contains("bikini") ||
                   s.Contains("leotard");
        }

        private static void LogSlotComponentSnapshot(string source, Transform t)
        {
            try
            {
                if (t == null) return;
                LogComponentSnapshot(source, t.gameObject);
                if (t.parent != null) LogComponentSnapshot(source + ":parent", t.parent.gameObject);
                var slot = FindCaSlotAncestor(t);
                if (slot != null && slot != t && slot != t.parent)
                    LogComponentSnapshot(source + ":slot", slot.gameObject);
            }
            catch { }
        }

        private static Transform FindCaSlotAncestor(Transform t)
        {
            try
            {
                for (var c = t; c != null; c = c.parent)
                {
                    string name = c.name ?? "";
                    if (name.ToLowerInvariant().Contains("ca_slot")) return c;
                }
            }
            catch { }
            return null;
        }

        private static void LogComponentSnapshot(string source, GameObject go)
        {
            try
            {
                if (go == null) return;
                string path = PathOf(go.transform);
                string key = $"slot-comp|{source}|{path}";
                lock (Sync)
                {
                    if (SlotComponentSnapshots.Count >= 256) return;
                    if (!SlotComponentSnapshots.Add(key)) return;
                }

                var components = go.GetComponents<Component>();
                LogLimited(
                    key,
                    $"[MeshSpy] SlotComponentSnapshot source=\"{source}\" go=\"{go.name}\" path=\"{path}\" active={go.activeSelf}/{go.activeInHierarchy} comps={components?.Length ?? 0}",
                    1);
                if (components == null) return;

                int shown = 0;
                for (int i = 0; i < components.Length && shown < 16; i++)
                {
                    var comp = components[i];
                    if (comp == null) continue;
                    string typeName = SafeTypeName(comp.GetType());
                    string values = ShouldInspectComponentValues(typeName) ? SummarizeComponentValues(comp, 24) : "built-in";
                    LogPlain($"[MeshSpy]  SlotComponent idx={i} type=\"{typeName}\" name=\"{comp.name}\" values=\"{values}\" path=\"{PathOf(comp.transform)}\"");
                    shown++;
                }
                if (components.Length > shown)
                    LogPlain($"[MeshSpy]  SlotComponent truncated shown={shown}/{components.Length}");
            }
            catch (Exception e)
            {
                LogLimited("slot-comp-error", "[MeshSpy] SlotComponentSnapshot error: " + Short(e.Message, 120), 6);
            }
        }

        private static bool ShouldInspectComponentValues(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return true;
            return !typeName.StartsWith("UnityEngine.", StringComparison.Ordinal) &&
                   !typeName.StartsWith("Il2CppUnityEngine.", StringComparison.Ordinal);
        }

        private static string SummarizeComponentValues(Component comp, int max)
        {
            try
            {
                var type = comp.GetType();
                var parts = new List<string>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (parts.Count >= max) break;
                    if (field == null || field.IsStatic || !IsSimpleMemberType(field.FieldType)) continue;
                    object value = null;
                    try { value = field.GetValue(comp); } catch { continue; }
                    parts.Add(field.Name + "=" + FormatSimpleValue(value));
                }

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (parts.Count >= max) break;
                    if (prop == null || !prop.CanRead || prop.GetIndexParameters().Length != 0 || !IsSimpleMemberType(prop.PropertyType)) continue;
                    object value = null;
                    try { value = prop.GetValue(comp, null); } catch { continue; }
                    parts.Add(prop.Name + "=" + FormatSimpleValue(value));
                }

                if (parts.Count == 0) return "no-simple-fields";
                return Short(string.Join("; ", parts), 900);
            }
            catch (Exception e)
            {
                return "inspect-fail:" + Short(e.Message, 120);
            }
        }

        private static bool IsSimpleMemberType(Type type)
        {
            try
            {
                if (type == null) return false;
                if (type.IsPrimitive || type.IsEnum) return true;
                if (type == typeof(string) || type == typeof(decimal)) return true;
                if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color)) return true;
                return typeof(UnityEngine.Object).IsAssignableFrom(type);
            }
            catch { return false; }
        }

        private static string FormatSimpleValue(object value)
        {
            try
            {
                if (value == null) return "null";
                if (value is string s) return "\"" + Short(s, 120) + "\"";
                if (value is Vector2 v2) return $"({v2.x:F3},{v2.y:F3})";
                if (value is Vector3 v3) return Fmt(v3);
                if (value is Vector4 v4) return $"({v4.x:F3},{v4.y:F3},{v4.z:F3},{v4.w:F3})";
                if (value is Quaternion q) return Fmt(q);
                if (value is Color c) return $"({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
                if (value is UnityEngine.Object uo)
                {
                    if (uo is Component component)
                        return $"{component.GetType().Name}:\"{component.name}\" path=\"{PathOf(component.transform)}\"";
                    if (uo is GameObject go)
                        return $"GameObject:\"{go.name}\" path=\"{PathOf(go.transform)}\"";
                    return $"{uo.GetType().Name}:\"{uo.name}\" id={ObjId(uo)}";
                }
                return Short(Convert.ToString(value), 160);
            }
            catch { return "value-error"; }
        }

        private static void SMRBlendShapeWeightPostfix(SkinnedMeshRenderer __instance, int index, float value)
        {
            try
            {
                if (__instance == null || __instance.sharedMesh == null) return;
                string key = $"smr-blend|{PathOf(__instance.transform)}|{__instance.sharedMesh.name}|{index}";
                LogLimited(key, $"[MeshSpy] SkinnedMeshRenderer.SetBlendShapeWeight index={index} value={value:F3} {DescribeSMR(__instance)}", 3);
            }
            catch { }
        }

        private static void FileReadAllBytesPostfix(string path, byte[] __result)
        {
            try
            {
                if (__result == null || !LooksInterestingAssetPath(path)) return;
                RememberBinarySource(__result, path);
                string key = $"file-readallbytes|{path}|{__result.Length}";
                LogLimited(key, $"[MeshSpy] AssetFlow.File.ReadAllBytes path=\"{path}\" bytes={__result.Length} {FileInfoOf(path)} binaryId={ManagedId(__result)}", 1);
            }
            catch { }
        }

        private static void FileOpenPostfix(object[] __args, FileStream __result)
        {
            try
            {
                if (__result == null) return;
                string path = FirstStringArg(__args);
                if (!LooksInterestingAssetPath(path)) return;
                RememberStreamSource(__result, path);
                string key = $"file-open|{path}|{ManagedId(__result)}";
                LogLimited(key, $"[MeshSpy] AssetFlow.File.Open path=\"{path}\" {FileInfoOf(path)} streamId={ManagedId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleLoadFromFilePostfix(string path, AssetBundle __result)
        {
            try
            {
                if (__result == null) return;
                string key = $"ab-loadfile|{path}|{__result.name}";
                RememberBundleSource(__result, path);
                if (LooksInterestingAssetPath(path))
                    LogLimited(key, $"[MeshSpy] AssetFlow.LoadFromFile path=\"{path}\" {FileInfoOf(path)} bundle=\"{__result.name}\" bundleId={ObjId(__result)}", 1);
                ProbeBundleSource("LoadFromFile", path, __result);
            }
            catch { }
        }

        private static void AssetBundleLoadFromFileAsyncPostfix(string path, AssetBundleCreateRequest __result)
        {
            try
            {
                if (__result == null) return;
                string key = $"ab-loadfile-async|{path}";
                RememberCreateRequestSource(__result, path);
                if (LooksInterestingAssetPath(path))
                    LogLimited(key, $"[MeshSpy] AssetFlow.LoadFromFileAsync path=\"{path}\" {FileInfoOf(path)} requestId={ObjId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleLoadFromMemoryPostfix(object[] __args, AssetBundle __result)
        {
            try
            {
                if (__result == null) return;
                string source = SourceOfFirstBinaryArg(__args);
                if (string.IsNullOrEmpty(source)) source = "memory";
                RememberBundleSource(__result, source);
                string key = $"ab-loadmem|{__result.name}";
                LogLimited(key, $"[MeshSpy] AssetFlow.LoadFromMemory source=\"{source}\" bundle=\"{__result.name}\" bundleId={ObjId(__result)}", 1);
                ProbeBundleSource("LoadFromMemory", source, __result);
            }
            catch { }
        }

        private static void AssetBundleCreateRequestResultPostfix(object[] __args, AssetBundleCreateRequest __result)
        {
            try
            {
                if (__result == null) return;
                string source = SourceOfFirstBinaryArg(__args);
                if (string.IsNullOrEmpty(source)) source = SourceOfFirstStreamArg(__args);
                if (string.IsNullOrEmpty(source)) source = "memory-or-stream-async";
                RememberCreateRequestSource(__result, source);
                string key = $"ab-create-request|{ObjId(__result)}";
                LogLimited(key, $"[MeshSpy] AssetFlow.CreateRequest source=\"{source}\" requestId={ObjId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleLoadFromStreamPostfix(object[] __args, AssetBundle __result)
        {
            try
            {
                if (__result == null) return;
                string source = SourceOfFirstStreamArg(__args);
                if (string.IsNullOrEmpty(source)) source = "stream";
                RememberBundleSource(__result, source);
                string key = $"ab-loadstream|{__result.name}";
                LogLimited(key, $"[MeshSpy] AssetFlow.LoadFromStream source=\"{source}\" bundle=\"{__result.name}\" bundleId={ObjId(__result)}", 1);
                ProbeBundleSource("LoadFromStream", source, __result);
            }
            catch { }
        }

        private static void AssetBundleLoadAssetPostfix(AssetBundle __instance, string name, UnityEngine.Object __result)
        {
            try
            {
                if (_suppressAssetLoadSpy) return;
                if (__result == null) return;
                string source = SourceOfBundle(__instance);
                TryReplaceLoadedClothMeshes(__instance, source, name, __result);
                LogObject("AssetBundle.LoadAsset", $"bundle=\"{__instance?.name}\" source=\"{source}\" asset=\"{name}\"", __result, 1);
            }
            catch { }
        }

        private static void TryReplaceLoadedClothMeshes(AssetBundle bundle, string source, string assetName, UnityEngine.Object result)
        {
            try
            {
                if (!LooksClothingBundleOrAsset(bundle?.name, source, assetName)) return;

                var go = result.TryCast<GameObject>();
                if (go == null) return;

                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs == null || smrs.Length == 0) return;

                int replaced = 0;
                int skipped = 0;
                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    if (smr?.sharedMesh == null) continue;
                    var original = smr.sharedMesh;
                    if (ShouldSkipClothMesh(smr, original, out string reason))
                    {
                        skipped++;
                        LogLimited(
                            $"cloth-readable-skip|{assetName}|{PathOf(smr.transform)}|{original.name}|{reason}",
                            $"[MeshSpy] ClothReadableReplace skip reason={reason} bundle=\"{bundle?.name}\" asset=\"{assetName}\" smr=\"{smr.name}\" mesh=\"{MeshInfo(original)}\" path=\"{PathOf(smr.transform)}\"",
                            1);
                        continue;
                    }

                    Mesh replacement = null;
                    string detail;
                    if (!TryBuildReadableClothMesh(smr, original, out replacement, out detail))
                    {
                        skipped++;
                        LogLimited(
                            $"cloth-readable-fail|{assetName}|{PathOf(smr.transform)}|{original.name}",
                            $"[MeshSpy] ClothReadableReplace fail {detail} bundle=\"{bundle?.name}\" asset=\"{assetName}\" smr=\"{smr.name}\" mesh=\"{MeshInfo(original)}\" path=\"{PathOf(smr.transform)}\"",
                            2);
                        continue;
                    }

                    RememberMeshSource(replacement, $"ClothReadableReplace bundle=\"{bundle?.name}\" source=\"{source}\" asset=\"{assetName}\" original=\"{original.name}\"");
                    smr.sharedMesh = replacement;
                    replaced++;
                    LogLimited(
                        $"cloth-readable-ok|{assetName}|{PathOf(smr.transform)}|{original.name}",
                        $"[MeshSpy] ClothReadableReplace ok bundle=\"{bundle?.name}\" asset=\"{assetName}\" go=\"{go.name}\" smr=\"{smr.name}\" original=\"{MeshInfo(original)}\" replacement=\"{MeshInfo(replacement)}\" {detail} path=\"{PathOf(smr.transform)}\"",
                        2);
                }

                if (replaced > 0 || skipped > 0)
                {
                    LogLimited(
                        $"cloth-readable-summary|{bundle?.name}|{assetName}|{go.name}",
                        $"[MeshSpy] ClothReadableReplace summary bundle=\"{bundle?.name}\" source=\"{source}\" asset=\"{assetName}\" go=\"{go.name}\" smrs={smrs.Length} replaced={replaced} skipped={skipped}",
                        1);
                    // InvalidateAll removed: do not trigger deform during cloth readable replace
                    // if (replaced > 0) BellyVertexMorph.InvalidateAll();
                }
            }
            catch (Exception e)
            {
                LogLimited("cloth-readable-error", "[MeshSpy] ClothReadableReplace error: " + Short(e.Message, 160), 8);
            }
        }

        private static bool LooksClothingBundleOrAsset(string bundleName, string source, string assetName)
        {
            string combined = ((bundleName ?? "") + "\n" + (source ?? "") + "\n" + (assetName ?? "")).ToLowerInvariant().Replace('\\', '/');
            if (combined.Contains("chara/body/")) return false;
            if (combined.Contains("co_nail")
                || combined.Contains("co_shoes")
                || combined.Contains("co_glove")
                || combined.Contains("co_hair")
                || combined.Contains("/head/")
                || combined.Contains("ao_head")
                || combined.Contains("ao_hair"))
                return false;

            return combined.Contains("chara/co_top")
                || combined.Contains("chara/co_bot")
                || combined.Contains("chara/co_bra")
                || combined.Contains("chara/co_shorts")
                || combined.Contains("chara/co_panst")
                || combined.Contains("chara/co_socks")
                || combined.Contains("cf_clothes")
                || combined.Contains("/clothes_")
                || combined.Contains("/n_clothes_");
        }

        private static bool ShouldSkipClothMesh(SkinnedMeshRenderer smr, Mesh mesh, out string reason)
        {
            reason = "";
            try
            {
                if (smr == null || mesh == null)
                {
                    reason = "null";
                    return true;
                }
                string id = ((smr.name ?? "") + "/" + (mesh.name ?? "") + "/" + PathOf(smr.transform)).ToLowerInvariant();
                if (id.Contains("nail") || id.Contains("shoe") ||
                    id.Contains("glove") || id.Contains("hair") ||
                    id.Contains("hand") || id.Contains("head") || id.Contains("face") ||
                    id.Contains("eye") || id.Contains("mayu") ||
                    id.Contains("o_tang") || id.Contains("dankon") || id.Contains("gomu"))
                {
                    reason = "non-cloth-body-or-extremity";
                    return true;
                }
                if (mesh.isReadable)
                {
                    reason = "already-readable";
                    return true;
                }
                if (mesh.vertexCount <= 0)
                {
                    reason = "zero-verts";
                    return true;
                }
                if (mesh.subMeshCount != 1)
                {
                    reason = "submesh-count-" + mesh.subMeshCount;
                    return true;
                }
                if (mesh.blendShapeCount > 0)
                {
                    reason = "blendshape-count-" + mesh.blendShapeCount;
                    return true;
                }
                try
                {
                    if (smr.bones == null || smr.bones.Length == 0)
                    {
                        reason = "no-bones";
                        return true;
                    }
                }
                catch
                {
                    reason = "bones-unreadable";
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                reason = "skip-error:" + Short(e.Message, 60);
                return true;
            }
        }

        private static bool TryBuildReadableClothMesh(SkinnedMeshRenderer smr, Mesh original, out Mesh replacement, out string detail)
        {
            Mesh baked = null;
            replacement = null;
            detail = "";
            try
            {
                baked = new Mesh();
                smr.BakeMesh(baked);

                var verts = baked.vertices;
                var tris = baked.triangles;
                if (verts == null || verts.Length == 0 || tris == null || tris.Length == 0)
                {
                    detail = $"bake-empty verts={verts?.Length ?? -1} tris={tris?.Length ?? -1}";
                    return false;
                }

                var boneWeights = original.boneWeights;
                if (boneWeights == null || boneWeights.Length != verts.Length)
                {
                    detail = $"boneWeights-len={boneWeights?.Length ?? -1} verts={verts.Length}";
                    return false;
                }

                var bindposes = original.bindposes;
                if (bindposes == null || bindposes.Length == 0)
                {
                    detail = "bindposes-empty";
                    return false;
                }

                // ── Z-up/Y-up mismatch correction ─────────────────────────────────
                // Some cloth meshes (e.g. o_cf_bot_denim00) are baked by BakeMesh in
                // Z-up orientation while their original bindposes are Y-up (identical
                // to the body's bindposes).  When this happens the GPU skinning formula
                //   rendered = bone_matrix * bindpose * vertex
                // receives a Z-up vertex with a Y-up bindpose, producing wrong results
                // at any non-rest-pose animation frame.
                //
                // Detection: compare the vertex centroid (from BakeMesh) against the
                // average bone origin derived from the bindposes:
                //   vtxZup  — vertex cloud is Z-up  (vtxCenter.z >> vtxCenter.y)
                //   bpYup   — bone origins are Y-up  (bpCenter.y  >> bpCenter.z)
                //
                // Fix: rotate baked vertices (and normals/tangents) from Z-up to Y-up
                //   (X,Y,Z) → (X, Z, -Y)
                // so they match the Y-up bindposes.  The bindposes and bone weights are
                // left unchanged; only the vertex positions/normals/tangents are fixed.
                bool didZupFix = false;
                {
                    // Sample vertex centroid (first 200 verts is enough)
                    Vector3 vtxCenter = Vector3.zero;
                    int sampleN = Mathf.Min(200, verts.Length);
                    for (int si = 0; si < sampleN; si++) vtxCenter += verts[si];
                    vtxCenter /= sampleN;

                    // Bindpose centroid — average bone origin in mesh local space
                    Vector3 bpCenter = Vector3.zero;
                    for (int bi = 0; bi < bindposes.Length; bi++)
                    {
                        var inv = bindposes[bi].inverse;
                        bpCenter += new Vector3(inv.m03, inv.m13, inv.m23);
                    }
                    bpCenter /= bindposes.Length;

                    bool vtxZup = vtxCenter.z >  0.3f && vtxCenter.z >  Mathf.Abs(vtxCenter.y) * 2f;
                    bool bpYup  = bpCenter.y  >  0.3f && bpCenter.y  >  Mathf.Abs(bpCenter.z)  * 2f;

                    if (vtxZup && bpYup)
                    {
                        // Pure rotation — determinant = +1, so normals transform the same way.
                        var normals  = baked.normals;
                        var tangents = baked.tangents;
                        bool hasNormals  = normals  != null && normals.Length  == verts.Length;
                        bool hasTangents = tangents != null && tangents.Length == verts.Length;

                        for (int i = 0; i < verts.Length; i++)
                        {
                            var v = verts[i];
                            verts[i] = new Vector3(v.x, v.z, -v.y);
                        }
                        if (hasNormals)
                        {
                            for (int i = 0; i < normals.Length; i++)
                            {
                                var n = normals[i];
                                normals[i] = new Vector3(n.x, n.z, -n.y);
                            }
                            baked.normals = normals;
                        }
                        if (hasTangents)
                        {
                            for (int i = 0; i < tangents.Length; i++)
                            {
                                var t = tangents[i];
                                tangents[i] = new Vector4(t.x, t.z, -t.y, t.w); // w = bitangent sign, unchanged
                            }
                            baked.tangents = tangents;
                        }
                        baked.vertices = verts;
                        try { baked.RecalculateBounds(); } catch { }
                        didZupFix = true;

                        LogLimited("cloth-zupfix", $"[MeshSpy] ClothReadableReplace ZupFix smr=\"{smr.name}\" vtxCenter={Fmt(vtxCenter)} bpCenter={Fmt(bpCenter)} — rotated verts (X,Y,Z)→(X,Z,-Y)", 4);
                    }
                }
                // ──────────────────────────────────────────────────────────────────

                var mesh = new Mesh();
                mesh.name = original.name;
                mesh.hideFlags = HideFlags.DontSave;
                mesh.vertices = verts;
                mesh.triangles = tris;
                TryCopyVector3Array("normals", baked.normals, verts.Length, a => mesh.normals = a);
                TryCopyVector4Array("tangents", baked.tangents, verts.Length, a => mesh.tangents = a);
                TryCopyVector2Array("uv", baked.uv, verts.Length, a => mesh.uv = a);
                TryCopyVector2Array("uv2", baked.uv2, verts.Length, a => mesh.uv2 = a);
                mesh.bindposes = bindposes;
                mesh.boneWeights = boneWeights;
                try { mesh.RecalculateBounds(); } catch { }

                replacement = mesh;
                detail = $"bakedVerts={verts.Length} tris={tris.Length} bindposes={bindposes.Length} boneWeights={boneWeights.Length} boundsC={Fmt(mesh.bounds.center)} boundsS={Fmt(mesh.bounds.size)} zupFix={didZupFix}";
                return true;
            }
            catch (Exception e)
            {
                detail = "exception:" + Short(e.Message, 120);
                DestroyTempMesh(replacement);
                replacement = null;
                return false;
            }
            finally
            {
                DestroyTempMesh(baked);
            }
        }

        private static void AssetBundleLoadAllAssetsPostfix(AssetBundle __instance, Il2CppReferenceArray<UnityEngine.Object> __result)
        {
            try
            {
                if (__result == null) return;
                int logged = 0;
                for (int i = 0; i < __result.Length && logged < 16; i++)
                {
                    var obj = __result[i];
                    if (obj == null) continue;
                    if (LogObject("AssetBundle.LoadAllAssets", $"bundle=\"{__instance?.name}\" source=\"{SourceOfBundle(__instance)}\" idx={i}", obj, 1))
                        logged++;
                }
            }
            catch { }
        }

        private static void AssetBundleLoadAllAssetsAsyncPostfix(AssetBundle __instance, AssetBundleRequest __result)
        {
            try
            {
                if (__result == null) return;
                string context = $"bundle=\"{__instance?.name}\" source=\"{SourceOfBundle(__instance)}\" allAssets";
                RememberAssetRequestSource(__result, context);
                string key = $"ab-all-async|{__instance?.name}|{ObjId(__result)}";
                LogLimited(key, $"[MeshSpy] AssetFlow.LoadAllAssetsAsync {context} requestId={ObjId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleLoadAssetWithSubAssetsPostfix(AssetBundle __instance, string name, Il2CppReferenceArray<UnityEngine.Object> __result)
        {
            try
            {
                if (__result == null) return;
                int logged = 0;
                for (int i = 0; i < __result.Length && logged < 16; i++)
                {
                    if (LogObject("AssetBundle.LoadAssetWithSubAssets", $"bundle=\"{__instance?.name}\" source=\"{SourceOfBundle(__instance)}\" asset=\"{name}\" idx={i}", __result[i], 1))
                        logged++;
                }
            }
            catch { }
        }

        private static void AssetBundleLoadAssetWithSubAssetsAsyncPostfix(AssetBundle __instance, string name, AssetBundleRequest __result)
        {
            try
            {
                if (__result == null) return;
                string context = $"bundle=\"{__instance?.name}\" source=\"{SourceOfBundle(__instance)}\" asset=\"{name}\" subAssets";
                RememberAssetRequestSource(__result, context);
                string key = $"ab-sub-async|{__instance?.name}|{name}|{ObjId(__result)}";
                if (LooksInteresting(name) || LooksInterestingAssetPath(SourceOfBundle(__instance)))
                    LogLimited(key, $"[MeshSpy] AssetFlow.LoadAssetWithSubAssetsAsync {context} requestId={ObjId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleLoadAssetAsyncPostfix(AssetBundle __instance, string name, AssetBundleRequest __result)
        {
            try
            {
                if (__result == null) return;
                string context = $"bundle=\"{__instance?.name}\" source=\"{SourceOfBundle(__instance)}\" asset=\"{name}\"";
                RememberAssetRequestSource(__result, context);
                string key = $"ab-async|{__instance?.name}|{name}|{ObjId(__result)}";
                if (LooksInteresting(name) || LooksInterestingAssetPath(SourceOfBundle(__instance)))
                    LogLimited(key, $"[MeshSpy] AssetFlow.LoadAssetAsync {context} requestId={ObjId(__result)}", 1);
            }
            catch { }
        }

        private static void AssetBundleRequestAssetPostfix(AssetBundleRequest __instance, UnityEngine.Object __result)
        {
            try
            {
                if (__result == null) return;
                string source = SourceOfAssetRequest(__instance);
                TryReplaceLoadedClothMeshes(null, source, "", __result);
                LogObject("AssetBundleRequest.asset", $"asyncResult {source}", __result, 1);
            }
            catch { }
        }

        private static void AssetBundleRequestAllAssetsPostfix(AssetBundleRequest __instance, Il2CppReferenceArray<UnityEngine.Object> __result)
        {
            try
            {
                if (__result == null) return;
                for (int i = 0; i < __result.Length && i < 16; i++)
                    LogObject("AssetBundleRequest.allAssets", $"{SourceOfAssetRequest(__instance)} idx={i}", __result[i], 1);
            }
            catch { }
        }

        private static void AssetBundleCreateRequestAssetBundlePostfix(AssetBundleCreateRequest __instance, AssetBundle __result)
        {
            try
            {
                if (__result == null) return;
                string source = SourceOfCreateRequest(__instance);
                RememberBundleSource(__result, source);
                string key = $"ab-create|{source}|{__result.name}|{ObjId(__result)}";
                if (LooksInterestingAssetPath(source))
                    LogLimited(key, $"[MeshSpy] AssetFlow.CreateRequest.assetBundle source=\"{source}\" bundle=\"{__result.name}\" bundleId={ObjId(__result)}", 1);
                ProbeBundleSource("CreateRequest.assetBundle", source, __result);
            }
            catch { }
        }

        private static void AssetBundleGetAllAssetNamesPostfix(AssetBundle __instance, Il2CppStringArray __result)
        {
            try
            {
                if (__instance == null || __result == null) return;
                LogBundleNameArray("AssetBundle.GetAllAssetNames", __instance, __result, 96);
            }
            catch { }
        }

        private static void AssetBundleGetAllScenePathsPostfix(AssetBundle __instance, Il2CppStringArray __result)
        {
            try
            {
                if (__instance == null || __result == null) return;
                LogBundleNameArray("AssetBundle.GetAllScenePaths", __instance, __result, 32);
            }
            catch { }
        }

        private static void AssetBundleContainsPostfix(AssetBundle __instance, string name, bool __result)
        {
            try
            {
                if (__instance == null || string.IsNullOrEmpty(name)) return;
                if (!__result && !LooksInteresting(name) && !LooksInterestingAssetPath(SourceOfBundle(__instance))) return;
                string key = $"ab-contains|{ObjId(__instance)}|{name}|{__result}";
                LogLimited(key, $"[MeshSpy] AssetBundle.Contains bundle=\"{__instance.name}\" source=\"{SourceOfBundle(__instance)}\" name=\"{name}\" result={__result}", 3);
            }
            catch { }
        }

        private static void AssetBundleUnloadPrefix(AssetBundle __instance, bool unloadAllLoadedObjects)
        {
            try
            {
                if (__instance == null) return;
                string source = SourceOfBundle(__instance);
                if (!LooksInterestingAssetPath(source) && !LooksInteresting(__instance.name)) return;
                string key = $"ab-unload|{ObjId(__instance)}|{unloadAllLoadedObjects}";
                LogLimited(key, $"[MeshSpy] AssetBundle.Unload bundle=\"{__instance.name}\" source=\"{source}\" unloadAllLoadedObjects={unloadAllLoadedObjects}", 2);
            }
            catch { }
        }

        private static void DownloadHandlerAssetBundleGetContentPostfix(UnityWebRequest www, AssetBundle __result)
        {
            try
            {
                if (__result == null) return;
                string url = "";
                try { url = www?.url ?? ""; } catch { }
                string source = string.IsNullOrEmpty(url) ? "DownloadHandlerAssetBundle" : url;
                RememberBundleSource(__result, source);
                string key = $"dh-ab-content|{source}|{__result.name}";
                if (LooksInterestingAssetPath(source))
                    LogLimited(key, $"[MeshSpy] AssetFlow.DownloadHandlerAssetBundle.GetContent source=\"{source}\" bundle=\"{__result.name}\" bundleId={ObjId(__result)}", 1);
                ProbeBundleSource("DownloadHandlerAssetBundle.GetContent", source, __result);
            }
            catch { }
        }

        private static void ResourcesLoadPostfix(string path, UnityEngine.Object __result)
        {
            try
            {
                if (__result == null) return;
                LogObject("Resources.Load", $"path=\"{path}\"", __result, 1);
            }
            catch { }
        }

        private static void ResourcesLoadAllPostfix(string path, Il2CppReferenceArray<UnityEngine.Object> __result)
        {
            try
            {
                if (__result == null) return;
                for (int i = 0; i < __result.Length && i < 16; i++)
                    LogObject("Resources.LoadAll", $"path=\"{path}\" idx={i}", __result[i], 1);
            }
            catch { }
        }

        private static void ResourcesLoadAsyncPostfix(string path, ResourceRequest __result)
        {
            try
            {
                if (__result == null) return;
                string key = $"res-async|{path}";
                if (LooksInteresting(path))
                    LogLimited(key, $"[MeshSpy] Resources.LoadAsync path=\"{path}\"", 1);
            }
            catch { }
        }

        private static void ResourceRequestAssetPostfix(ResourceRequest __instance, UnityEngine.Object __result)
        {
            try
            {
                if (__result == null) return;
                LogObject("ResourceRequest.asset", "asyncResult", __result, 1);
            }
            catch { }
        }

        private static void ObjectInstantiatePostfix(UnityEngine.Object __result)
        {
            try
            {
                if (__result == null) return;
                LogObject("Object.Instantiate", "result", __result, 1);
            }
            catch { }
        }

        private static void MeshVerticesPostfix(Mesh __instance, Il2CppStructArray<Vector3> value)
        {
            try
            {
                string key = $"mesh-vertices|{__instance.name}|{__instance.vertexCount}";
                LogLimited(key, $"[MeshSpy] Mesh.set_vertices mesh=\"{MeshInfo(__instance)}\" valueLen={value?.Length ?? -1}", 4);
            }
            catch { }
        }

        private static void MeshMutationPostfix(Mesh __instance, MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (__instance == null) return;
                string method = __originalMethod != null ? __originalMethod.Name : "unknown";
                string key = $"mesh-mut|{method}|{__instance.name}|{__instance.vertexCount}";
                LogLimited(key, $"[MeshSpy] Mesh.{method} mesh=\"{MeshInfo(__instance)}\" args={ArgSummary(__args, 5)} source=\"{SourceOfMesh(__instance)}\"", 4);
            }
            catch { }
        }

        private static void MeshUploadMeshDataPostfix(Mesh __instance, bool markNoLongerReadable)
        {
            try
            {
                if (__instance == null) return;
                string key = $"mesh-upload|{__instance.name}|{__instance.vertexCount}|{markNoLongerReadable}";
                LogLimited(key, $"[MeshSpy] Mesh.UploadMeshData markNoLongerReadable={markNoLongerReadable} mesh=\"{MeshInfo(__instance)}\"", 3);
            }
            catch { }
        }

        private static void CustomShapeMethodPostfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                if (__originalMethod == null) return;
                string methodName = __originalMethod.Name ?? "";
                string typeName = __originalMethod.DeclaringType != null ? __originalMethod.DeclaringType.FullName : "(unknown)";
                if (!LooksInterestingCustomCall(__instance, __args, methodName, typeName)) return;
                string key = $"custom-call|{typeName}|{methodName}|{ArgKey(__args)}";
                LogLimited(key, $"[MeshSpy] CustomShapeCall {typeName}.{methodName} this={ObjectSummary(__instance)} args={ArgSummary(__args, 8)}", 4);
            }
            catch { }
        }

        private static bool LogObject(string kind, string context, UnityEngine.Object obj, int limit)
        {
            if (!SpyLogEnabled) return false;
            if (obj == null) return false;
            try
            {
                var go = obj.TryCast<GameObject>();
                if (go != null) return LogGameObject(kind, context, go, limit);
            }
            catch { }

            try
            {
                var mesh = obj.TryCast<Mesh>();
                if (mesh != null)
                {
                    string key = $"{kind}|mesh|{context}|{mesh.name}|{mesh.vertexCount}";
                    LogLimited(key, $"[MeshSpy] {kind} {context} object=Mesh mesh=\"{MeshInfo(mesh)}\"", limit);
                    return true;
                }
            }
            catch { }

            try
            {
                if (LooksInteresting(obj.name))
                {
                    string key = $"{kind}|obj|{context}|{obj.name}";
                    LogLimited(key, $"[MeshSpy] {kind} {context} object=\"{obj.name}\"", limit);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string ObjId(UnityEngine.Object obj)
        {
            try { return obj == null ? "0x0" : "0x" + obj.Pointer.ToString("X"); }
            catch { return "0x?"; }
        }

        private static string ObjId(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj)
        {
            try { return obj == null ? "0x0" : "0x" + obj.Pointer.ToString("X"); }
            catch { return "0x?"; }
        }

        private static string ManagedId(object obj)
        {
            try { return obj == null ? "0x0" : "m" + RuntimeHelpers.GetHashCode(obj).ToString("X"); }
            catch { return "m?"; }
        }

        private static string FirstStringArg(object[] args)
        {
            try
            {
                if (args == null) return "";
                foreach (var arg in args)
                    if (arg is string s)
                        return s;
            }
            catch { }
            return "";
        }

        private static string SourceOfFirstBinaryArg(object[] args)
        {
            try
            {
                if (args == null) return "";
                foreach (var arg in args)
                    if (arg is byte[] bytes)
                        return SourceOfBinary(bytes);
            }
            catch { }
            return "";
        }

        private static string SourceOfFirstStreamArg(object[] args)
        {
            try
            {
                if (args == null) return "";
                foreach (var arg in args)
                    if (arg is Stream stream)
                        return SourceOfStream(stream);
            }
            catch { }
            return "";
        }

        private static void RememberBundleSource(AssetBundle bundle, string source)
        {
            if (bundle == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) BundleSources[ObjId(bundle)] = source;
            }
            catch { }
        }

        private static string SourceOfBundle(AssetBundle bundle)
        {
            if (bundle == null) return "";
            try
            {
                lock (Sync)
                {
                    if (BundleSources.TryGetValue(ObjId(bundle), out var source) && !string.IsNullOrEmpty(source))
                        return source;
                }

                string fallback = ResolveBundleNameToPath(bundle.name);
                if (!string.IsNullOrEmpty(fallback))
                    RememberBundleSource(bundle, fallback);
                return fallback;
            }
            catch { return ""; }
        }

        private static string ResolveBundleNameToPath(string bundleName)
        {
            try
            {
                if (string.IsNullOrEmpty(bundleName)) return "";
                string normalized = bundleName.Replace('\\', '/');
                if (Path.IsPathRooted(normalized)) return normalized;
                if (!normalized.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase)
                    && !normalized.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                    && !normalized.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase))
                    return "";

                string dataPath = "";
                try { dataPath = Application.dataPath; } catch { }
                if (string.IsNullOrEmpty(dataPath)) return "";

                string root = Path.GetFullPath(Path.Combine(dataPath, ".."));
                string relative = normalized.StartsWith("abdata/", StringComparison.OrdinalIgnoreCase)
                    ? normalized.Substring("abdata/".Length)
                    : normalized;
                string full = Path.GetFullPath(Path.Combine(root, "abdata", relative.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(full)) return full;
                return full;
            }
            catch { return ""; }
        }

        private static void RememberCreateRequestSource(AssetBundleCreateRequest request, string source)
        {
            if (request == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) CreateRequestSources[ObjId(request)] = source;
            }
            catch { }
        }

        private static string SourceOfCreateRequest(AssetBundleCreateRequest request)
        {
            if (request == null) return "";
            try
            {
                lock (Sync)
                    return CreateRequestSources.TryGetValue(ObjId(request), out var source) ? source : "";
            }
            catch { return ""; }
        }

        private static void RememberAssetRequestSource(AssetBundleRequest request, string source)
        {
            if (request == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) AssetRequestSources[ObjId(request)] = source;
            }
            catch { }
        }

        private static string SourceOfAssetRequest(AssetBundleRequest request)
        {
            if (request == null) return "";
            try
            {
                lock (Sync)
                    return AssetRequestSources.TryGetValue(ObjId(request), out var source) ? source : "";
            }
            catch { return ""; }
        }

        private static void RememberBinarySource(byte[] data, string source)
        {
            if (data == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) BinarySources[ManagedId(data)] = source;
            }
            catch { }
        }

        private static string SourceOfBinary(byte[] data)
        {
            if (data == null) return "";
            try
            {
                lock (Sync)
                    return BinarySources.TryGetValue(ManagedId(data), out var source) ? source : "";
            }
            catch { return ""; }
        }

        private static void RememberStreamSource(Stream stream, string source)
        {
            if (stream == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) StreamSources[ManagedId(stream)] = source;
            }
            catch { }
        }

        private static string SourceOfStream(Stream stream)
        {
            if (stream == null) return "";
            try
            {
                lock (Sync)
                    return StreamSources.TryGetValue(ManagedId(stream), out var source) ? source : "";
            }
            catch { return ""; }
        }

        private static void RememberMeshSource(Mesh mesh, string source)
        {
            if (mesh == null || string.IsNullOrEmpty(source)) return;
            try
            {
                lock (Sync) MeshSources[ObjId(mesh)] = source;
            }
            catch { }
        }

        private static string SourceOfMesh(Mesh mesh)
        {
            if (mesh == null) return "";
            try
            {
                lock (Sync)
                    return MeshSources.TryGetValue(ObjId(mesh), out var source) ? source : "";
            }
            catch { return ""; }
        }

        private static void ProbeBundleSource(string tag, string source, AssetBundle bundle)
        {
            try
            {
                if (bundle == null) return;
                if (!LooksInterestingAssetPath(source) && !LooksInteresting(bundle.name)) return;

                string bundleKey = ObjId(bundle) + "|" + (source ?? "") + "|" + (bundle.name ?? "");
                lock (Sync)
                {
                    if (ProbedBundles.Count >= 128) return;
                    if (!ProbedBundles.Add(bundleKey)) return;
                }

                string disk = ProbeDiskAssetFile(source);
                LogLimited(
                    $"bundle-probe|{bundleKey}",
                    $"[MeshSpy] BundleDiskProbe tag=\"{tag}\" bundle=\"{bundle.name}\" bundleId={ObjId(bundle)} source=\"{source}\" {disk}",
                    1);

                try
                {
                    var names = bundle.GetAllAssetNames();
                    LogBundleNameArray("BundleIndexProbe.GetAllAssetNames", bundle, names, 128);
                }
                catch (Exception e)
                {
                    LogLimited($"bundle-index-fail|{bundleKey}", $"[MeshSpy] BundleIndexProbe.GetAllAssetNames failed bundle=\"{bundle.name}\" source=\"{source}\" error=\"{Short(e.Message, 120)}\"", 1);
                }

                try
                {
                    var scenes = bundle.GetAllScenePaths();
                    if (scenes != null && scenes.Length > 0)
                        LogBundleNameArray("BundleIndexProbe.GetAllScenePaths", bundle, scenes, 64);
                }
                catch { }
            }
            catch (Exception e)
            {
                LogLimited("bundle-probe-error", "[MeshSpy] BundleDiskProbe failed: " + e.Message, 4);
            }
        }

        private static string ProbeDiskAssetFile(string source)
        {
            try
            {
                if (string.IsNullOrEmpty(source)) return "disk=none";
                if (source.StartsWith("memory", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("stream", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return "disk=not-file";

                string path = source.Replace('/', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(path)) path = ResolveBundleNameToPath(source);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "disk=file-missing";

                var fi = new FileInfo(path);
                int readLen = (int)Math.Min(fi.Length, MaxDiskProbeBytes);
                var bytes = new byte[readLen];
                using (var fs = File.OpenRead(path))
                {
                    int offset = 0;
                    while (offset < readLen)
                    {
                        int got = fs.Read(bytes, offset, readLen - offset);
                        if (got <= 0) break;
                        offset += got;
                    }
                    if (offset != readLen && offset >= 0)
                    {
                        var trimmed = new byte[offset];
                        Array.Copy(bytes, trimmed, offset);
                        bytes = trimmed;
                    }
                }

                return $"disk=file bytesRead={bytes.Length}/{fi.Length} header=\"{DescribeBundleHeader(bytes)}\" asciiHits={ExtractAsciiHits(bytes, 24)}";
            }
            catch (Exception e)
            {
                return "disk=probe-fail:" + Short(e.Message, 100);
            }
        }

        private static string DescribeBundleHeader(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0) return "empty";
                var parts = new List<string>();
                int pos = 0;
                for (int i = 0; i < 4 && pos < bytes.Length; i++)
                {
                    int start = pos;
                    while (pos < bytes.Length && bytes[pos] != 0 && pos - start < 160) pos++;
                    if (pos <= start) break;
                    parts.Add(Short(Encoding.ASCII.GetString(bytes, start, pos - start), 160));
                    pos++;
                }

                if (parts.Count > 0) return string.Join("|", parts);
                int count = Math.Min(bytes.Length, 16);
                var hex = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) hex.Append(' ');
                    hex.Append(bytes[i].ToString("X2"));
                }
                return "hex:" + hex;
            }
            catch { return "header-error"; }
        }

        private static string ExtractAsciiHits(byte[] bytes, int maxHits)
        {
            try
            {
                if (bytes == null || bytes.Length == 0) return "[]";
                var hits = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sb = new StringBuilder();

                for (int i = 0; i <= bytes.Length; i++)
                {
                    byte b = i < bytes.Length ? bytes[i] : (byte)0;
                    bool printable = b >= 32 && b <= 126;
                    if (printable)
                    {
                        if (sb.Length < 220) sb.Append((char)b);
                        continue;
                    }

                    if (sb.Length >= 4)
                    {
                        string s = sb.ToString();
                        if ((LooksInteresting(s) || LooksInterestingAssetPath(s)) && seen.Add(s))
                        {
                            hits.Add("\"" + Short(s, 120) + "\"");
                            if (hits.Count >= maxHits) break;
                        }
                    }
                    sb.Length = 0;
                }

                return "[" + string.Join(", ", hits) + "]";
            }
            catch { return "[ascii-scan-error]"; }
        }

        private static void LogBundleNameArray(string kind, AssetBundle bundle, Il2CppStringArray names, int maxShown)
        {
            try
            {
                if (bundle == null || names == null) return;
                string source = SourceOfBundle(bundle);
                bool sourceInteresting = LooksInterestingAssetPath(source) || LooksInteresting(bundle.name);
                var shown = new List<string>();
                int interesting = 0;
                int len = 0;
                try { len = names.Length; } catch { }
                for (int i = 0; i < len; i++)
                {
                    string name = "";
                    try { name = names[i]; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;
                    bool nameInteresting = LooksInteresting(name) || LooksInterestingAssetPath(name);
                    if (nameInteresting) interesting++;
                    if ((nameInteresting || sourceInteresting) && shown.Count < maxShown)
                        shown.Add("\"" + Short(name, 140) + "\"");
                }

                if (!sourceInteresting && interesting == 0) return;
                string key = $"{kind}|{ObjId(bundle)}|{source}|{len}";
                LogLimited(key, $"[MeshSpy] {kind} bundle=\"{bundle.name}\" source=\"{source}\" count={len} interesting={interesting} shown=[{string.Join(", ", shown)}]", 1);
            }
            catch { }
        }

        private static void ProbeDiskMeshRoute(AssetBundle bundle, string source, string assetName, GameObject go)
        {
            try
            {
                if (bundle == null || go == null) return;
                if (!LooksCharacterMeshAssetPath(source) && !LooksInteresting(assetName) && !LooksInteresting(go.name)) return;

                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs == null || smrs.Length == 0) return;

                var interestingSmrs = new List<SkinnedMeshRenderer>();
                var meshNames = new List<string>();
                foreach (var smr in smrs)
                {
                    if (smr?.sharedMesh == null) continue;
                    if (!LooksInteresting(smr.name) && !LooksInteresting(smr.sharedMesh.name)) continue;
                    interestingSmrs.Add(smr);
                    if (!string.IsNullOrEmpty(smr.sharedMesh.name) && !meshNames.Contains(smr.sharedMesh.name))
                        meshNames.Add(smr.sharedMesh.name);
                    if (interestingSmrs.Count >= 8) break;
                }
                if (interestingSmrs.Count == 0) return;

                string key = $"{ObjId(bundle)}|{source}|{assetName}|{go.name}";
                lock (Sync)
                {
                    if (ProbedDiskMeshRoutes.Count >= 32) return;
                    if (!ProbedDiskMeshRoutes.Add(key)) return;
                }

                var bundleNames = GetBundleAssetNames(bundle);
                string disk = ProbeDiskMeshFile(source, assetName, go.name, meshNames);
                string index = SummarizeBundleIndexMatches(bundleNames, assetName, go.name, meshNames);
                LogLimited(
                    $"disk-mesh-route|{key}",
                    $"[MeshSpy] DiskMeshRoute bundle=\"{bundle.name}\" source=\"{source}\" asset=\"{assetName}\" go=\"{go.name}\" smrs={smrs.Length} targetSmrs={interestingSmrs.Count} {disk} {index}",
                    1);

                foreach (var smr in interestingSmrs)
                    LogDiskMeshSMR(bundle, source, assetName, smr, bundleNames);
            }
            catch (Exception e)
            {
                LogLimited("disk-mesh-route-error", "[MeshSpy] DiskMeshRoute failed: " + Short(e.Message, 160), 6);
            }
        }

        private static void LogDiskMeshSMR(AssetBundle bundle, string source, string assetName, SkinnedMeshRenderer smr, List<string> bundleNames)
        {
            try
            {
                var mesh = smr?.sharedMesh;
                if (mesh == null) return;
                string key = $"{ObjId(bundle)}|{assetName}|{PathOf(smr.transform)}|{mesh.name}|{mesh.vertexCount}";
                string original = ProbeMeshChannels(mesh);
                string baked = ProbeBakeMeshDetailed(smr, mesh, out string candidate);
                string directBundleMesh = ProbeBundleMeshAssetLoad(bundle, mesh.name, bundleNames);
                LogLimited(
                    $"disk-mesh-smr|{key}",
                    $"[MeshSpy] DiskMeshSMR asset=\"{assetName}\" smr=\"{smr.name}\" path=\"{PathOf(smr.transform)}\" mesh=\"{MeshInfo(mesh)}\" source=\"{source}\" original={original} baked={baked} readableCandidate={candidate} bundleMesh={directBundleMesh}",
                    1);
            }
            catch (Exception e)
            {
                LogLimited("disk-mesh-smr-error", "[MeshSpy] DiskMeshSMR failed: " + Short(e.Message, 160), 8);
            }
        }

        private static List<string> GetBundleAssetNames(AssetBundle bundle)
        {
            var result = new List<string>();
            try
            {
                if (bundle == null) return result;
                var names = bundle.GetAllAssetNames();
                if (names == null) return result;
                for (int i = 0; i < names.Length; i++)
                {
                    string name = "";
                    try { name = names[i]; } catch { }
                    if (!string.IsNullOrEmpty(name))
                        result.Add(name);
                }
            }
            catch { }
            return result;
        }

        private static string SummarizeBundleIndexMatches(List<string> bundleNames, string assetName, string goName, List<string> meshNames)
        {
            try
            {
                if (bundleNames == null || bundleNames.Count == 0) return "index=count=0";
                var shown = new List<string>();
                int matches = 0;
                foreach (var name in bundleNames)
                {
                    if (!NameMatchesAnyNeedle(name, assetName, goName, meshNames)) continue;
                    matches++;
                    if (shown.Count < 24)
                        shown.Add("\"" + Short(name, 140) + "\"");
                }
                return $"index=count={bundleNames.Count} matches={matches} shown=[{string.Join(", ", shown)}]";
            }
            catch { return "index=error"; }
        }

        private static bool NameMatchesAnyNeedle(string name, string assetName, string goName, List<string> meshNames)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return false;
                string n = name.ToLowerInvariant();
                if (!string.IsNullOrEmpty(assetName) && n.Contains(assetName.ToLowerInvariant())) return true;
                if (!string.IsNullOrEmpty(goName) && n.Contains(goName.ToLowerInvariant())) return true;
                if (meshNames != null)
                {
                    foreach (var meshName in meshNames)
                        if (!string.IsNullOrEmpty(meshName) && n.Contains(meshName.ToLowerInvariant()))
                            return true;
                }
            }
            catch { }
            return false;
        }

        private static string ProbeDiskMeshFile(string source, string assetName, string goName, List<string> meshNames)
        {
            try
            {
                string path = ResolveDiskSourcePath(source);
                if (string.IsNullOrEmpty(path)) return "diskMesh=file-unresolved";
                if (!File.Exists(path)) return "diskMesh=file-missing";

                var fi = new FileInfo(path);
                var needles = new List<string>();
                AddNeedle(needles, assetName);
                AddNeedle(needles, goName);
                if (meshNames != null)
                    foreach (var meshName in meshNames)
                        AddNeedle(needles, meshName);

                string header = DescribeUnityFSFileHeader(path);
                string hits = FindNeedlesInFile(path, needles, MaxDiskMeshStringScanBytes, 32);
                return $"diskMesh=file size={fi.Length} header={header} stringScan={hits}";
            }
            catch (Exception e)
            {
                return "diskMesh=probe-fail:" + Short(e.Message, 120);
            }
        }

        private static void AddNeedle(List<string> needles, string value)
        {
            if (needles == null || string.IsNullOrEmpty(value)) return;
            if (!needles.Contains(value)) needles.Add(value);
            string lower = value.ToLowerInvariant();
            if (!needles.Contains(lower)) needles.Add(lower);
        }

        private static string ProbeMeshChannels(Mesh mesh)
        {
            if (mesh == null) return "mesh=null";
            var parts = new List<string>();
            try { parts.Add("readable=" + mesh.isReadable); } catch { parts.Add("readable=?"); }
            try { parts.Add("verts=" + (mesh.vertices?.Length ?? -1)); } catch (Exception e) { parts.Add("verts=fail:" + Short(e.Message, 50)); }
            try { parts.Add("tris=" + (mesh.triangles?.Length ?? -1)); } catch (Exception e) { parts.Add("tris=fail:" + Short(e.Message, 50)); }
            try { parts.Add("normals=" + (mesh.normals?.Length ?? -1)); } catch (Exception e) { parts.Add("normals=fail:" + Short(e.Message, 50)); }
            try { parts.Add("tangents=" + (mesh.tangents?.Length ?? -1)); } catch (Exception e) { parts.Add("tangents=fail:" + Short(e.Message, 50)); }
            try { parts.Add("uv=" + (mesh.uv?.Length ?? -1)); } catch (Exception e) { parts.Add("uv=fail:" + Short(e.Message, 50)); }
            try { parts.Add("boneWeights=" + (mesh.boneWeights?.Length ?? -1)); } catch (Exception e) { parts.Add("boneWeights=fail:" + Short(e.Message, 50)); }
            try { parts.Add("bindposes=" + (mesh.bindposes?.Length ?? -1)); } catch (Exception e) { parts.Add("bindposes=fail:" + Short(e.Message, 50)); }
            try { parts.Add("subMeshes=" + mesh.subMeshCount); } catch { parts.Add("subMeshes=?"); }
            try { parts.Add("blendShapes=" + mesh.blendShapeCount); } catch { parts.Add("blendShapes=?"); }
            return "{" + string.Join(" ", parts) + "}";
        }

        private static string ProbeBakeMeshDetailed(SkinnedMeshRenderer smr, Mesh original, out string candidateSummary)
        {
            Mesh baked = null;
            candidateSummary = "not-run";
            try
            {
                baked = new Mesh();
                smr.BakeMesh(baked);
                candidateSummary = ProbeReadableCandidateFromBake(original, baked);
                return ProbeMeshChannels(baked) + $" boundsC={Fmt(baked.bounds.center)} boundsS={Fmt(baked.bounds.size)}";
            }
            catch (Exception e)
            {
                candidateSummary = "not-run:bake-failed";
                return "fail:" + Short(e.Message, 100);
            }
            finally
            {
                DestroyTempMesh(baked);
            }
        }

        private static string ProbeReadableCandidateFromBake(Mesh original, Mesh baked)
        {
            Mesh candidate = null;
            try
            {
                if (baked == null) return "fail:no-baked";
                var verts = baked.vertices;
                var tris = baked.triangles;
                if (verts == null || verts.Length == 0 || tris == null || tris.Length == 0)
                    return $"fail:baked-data verts={verts?.Length ?? -1} tris={tris?.Length ?? -1}";

                candidate = new Mesh();
                candidate.name = (original?.name ?? "mesh") + "_spyReadableCandidate";
                candidate.vertices = verts;
                candidate.triangles = tris;

                TryCopyVector3Array("normals", baked.normals, verts.Length, a => candidate.normals = a);
                TryCopyVector4Array("tangents", baked.tangents, verts.Length, a => candidate.tangents = a);
                TryCopyVector2Array("uv", baked.uv, verts.Length, a => candidate.uv = a);
                TryCopyVector2Array("uv2", baked.uv2, verts.Length, a => candidate.uv2 = a);

                string bindposes = "none";
                string boneWeights = "none";
                try
                {
                    var bp = original?.bindposes;
                    if (bp != null && bp.Length > 0)
                    {
                        candidate.bindposes = bp;
                        bindposes = "copied:" + bp.Length;
                    }
                }
                catch (Exception e) { bindposes = "fail:" + Short(e.Message, 60); }

                try
                {
                    var bw = original?.boneWeights;
                    if (bw != null && bw.Length == verts.Length)
                    {
                        candidate.boneWeights = bw;
                        boneWeights = "copied:" + bw.Length;
                    }
                    else if (bw != null)
                    {
                        boneWeights = "wrong-len:" + bw.Length;
                    }
                }
                catch (Exception e) { boneWeights = "fail:" + Short(e.Message, 60); }

                try { candidate.RecalculateBounds(); } catch { }
                return $"ok mesh=\"{MeshInfo(candidate)}\" channels={ProbeMeshChannels(candidate)} skin=bindposes:{bindposes},boneWeights:{boneWeights}";
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 100);
            }
            finally
            {
                DestroyTempMesh(candidate);
            }
        }

        private static void TryCopyVector3Array(string name, Vector3[] data, int expected, Action<Vector3[]> setter)
        {
            try { if (data != null && data.Length == expected) setter(data); } catch { }
        }

        private static void TryCopyVector4Array(string name, Vector4[] data, int expected, Action<Vector4[]> setter)
        {
            try { if (data != null && data.Length == expected) setter(data); } catch { }
        }

        private static void TryCopyVector2Array(string name, Vector2[] data, int expected, Action<Vector2[]> setter)
        {
            try { if (data != null && data.Length == expected) setter(data); } catch { }
        }

        private static string ProbeBundleMeshAssetLoad(AssetBundle bundle, string meshName, List<string> bundleNames)
        {
            try
            {
                if (bundle == null || string.IsNullOrEmpty(meshName)) return "skip";
                var candidates = BuildMeshAssetNameCandidates(meshName, bundleNames);
                if (candidates.Count == 0) candidates.Add(meshName);

                var shown = new List<string>();
                int tried = 0;
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    if (tried++ >= 4) break;
                    Mesh loaded = null;
                    try
                    {
                        _suppressAssetLoadSpy = true;
                        var loadedObj = bundle.LoadAsset(candidate, Il2CppType.Of<Mesh>());
                        loaded = loadedObj != null ? loadedObj.TryCast<Mesh>() : null;
                    }
                    catch (Exception e)
                    {
                        shown.Add($"\"{Short(candidate, 80)}\"=fail:{Short(e.Message, 60)}");
                        continue;
                    }
                    finally
                    {
                        _suppressAssetLoadSpy = false;
                    }

                    if (loaded != null)
                    {
                        shown.Add($"\"{Short(candidate, 80)}\"={MeshInfo(loaded)} {ProbeMeshChannels(loaded)}");
                        break;
                    }
                    shown.Add($"\"{Short(candidate, 80)}\"=null");
                }

                return "{" + string.Join("; ", shown) + "}";
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 100);
            }
            finally
            {
                _suppressAssetLoadSpy = false;
            }
        }

        private static List<string> BuildMeshAssetNameCandidates(string meshName, List<string> bundleNames)
        {
            var result = new List<string>();
            try
            {
                if (bundleNames != null)
                {
                    string lowerMesh = meshName.ToLowerInvariant();
                    foreach (var name in bundleNames)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        string lower = name.ToLowerInvariant();
                        string file = Path.GetFileNameWithoutExtension(lower);
                        if (lower.Contains(lowerMesh) || file == lowerMesh)
                            result.Add(name);
                        if (result.Count >= 8) break;
                    }
                }
                if (!result.Contains(meshName)) result.Add(meshName);
            }
            catch { }
            return result;
        }

        private static string ResolveDiskSourcePath(string source)
        {
            try
            {
                if (string.IsNullOrEmpty(source)) return "";
                if (source.StartsWith("memory", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("stream", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return "";
                string path = source.Replace('/', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(path)) path = ResolveBundleNameToPath(source);
                return path;
            }
            catch { return ""; }
        }

        private static string DescribeUnityFSFileHeader(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
                string sig = ReadCString(br, 32);
                if (sig != "UnityFS")
                    return $"\"{Short(sig, 32)}\"";
                uint format = ReadUInt32BE(br);
                string unityVersion = ReadCString(br, 128);
                string unityRevision = ReadCString(br, 128);
                ulong fileSize = ReadUInt64BE(br);
                uint compressedInfo = ReadUInt32BE(br);
                uint uncompressedInfo = ReadUInt32BE(br);
                uint flags = ReadUInt32BE(br);
                uint compression = flags & 0x3F;
                return $"UnityFS(format={format},unity=\"{Short(unityVersion, 48)}\",rev=\"{Short(unityRevision, 48)}\",fileSize={fileSize},blockInfo={compressedInfo}/{uncompressedInfo},flags=0x{flags:X},compression={compression})";
            }
            catch (Exception e)
            {
                return "header-fail:" + Short(e.Message, 80);
            }
        }

        private static string FindNeedlesInFile(string path, List<string> needles, int maxBytes, int maxHits)
        {
            try
            {
                if (needles == null || needles.Count == 0) return "needles=0";
                var fi = new FileInfo(path);
                int readLen = (int)Math.Min(fi.Length, maxBytes);
                var bytes = new byte[readLen];
                using (var fs = File.OpenRead(path))
                {
                    int offset = 0;
                    while (offset < readLen)
                    {
                        int got = fs.Read(bytes, offset, readLen - offset);
                        if (got <= 0) break;
                        offset += got;
                    }
                    if (offset < readLen)
                    {
                        var trimmed = new byte[offset];
                        Array.Copy(bytes, trimmed, offset);
                        bytes = trimmed;
                    }
                }

                var hits = new List<string>();
                foreach (var needle in needles)
                {
                    if (string.IsNullOrEmpty(needle)) continue;
                    var n = Encoding.UTF8.GetBytes(needle);
                    int pos = IndexOfBytes(bytes, n);
                    if (pos >= 0)
                    {
                        hits.Add($"\"{Short(needle, 80)}\"@0x{pos:X}");
                        if (hits.Count >= maxHits) break;
                    }
                }

                return $"scanned={bytes.Length}/{fi.Length} hits=[{string.Join(", ", hits)}]";
            }
            catch (Exception e)
            {
                return "scan-fail:" + Short(e.Message, 100);
            }
        }

        private static int IndexOfBytes(byte[] haystack, byte[] needle)
        {
            try
            {
                if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length) return -1;
                for (int i = 0; i <= haystack.Length - needle.Length; i++)
                {
                    int j = 0;
                    while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                    if (j == needle.Length) return i;
                }
            }
            catch { }
            return -1;
        }

        private static string ReadCString(BinaryReader br, int max)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < max; i++)
            {
                int b = br.ReadByte();
                if (b == 0) break;
                bytes.Add((byte)b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static uint ReadUInt32BE(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length != 4) throw new EndOfStreamException();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static ulong ReadUInt64BE(BinaryReader br)
        {
            var b = br.ReadBytes(8);
            if (b.Length != 8) throw new EndOfStreamException();
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value = (value << 8) | b[i];
            return value;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            if (assembly == null) yield break;
            Type[] types = null;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { }
            if (types == null) yield break;
            foreach (var type in types)
                if (type != null)
                    yield return type;
        }

        private static bool IsLikelyCustomShapeType(Type type)
        {
            try
            {
                if (type == null) return false;
                string s = ((type.FullName ?? "") + "/" + (type.Namespace ?? "") + "/" + type.Name).ToLowerInvariant();
                return s.Contains("character")
                    || s.Contains("human")
                    || s.Contains("body")
                    || s.Contains("cloth")
                    || s.Contains("clothes")
                    || s.Contains("wear")
                    || s.Contains("custom")
                    || s.Contains("shape")
                    || s.Contains("coordinate")
                    || s.Contains("chara");
            }
            catch { return false; }
        }

        private static bool IsLikelyAssetPipelineType(Type type)
        {
            try
            {
                if (type == null) return false;
                string s = ((type.FullName ?? "") + "/" + (type.Namespace ?? "") + "/" + type.Name).ToLowerInvariant();
                return s.Contains("asset")
                    || s.Contains("bundle")
                    || s.Contains("resource")
                    || s.Contains("loader")
                    || s.Contains("file")
                    || s.Contains("path")
                    || s.Contains("character")
                    || s.Contains("human")
                    || s.Contains("body")
                    || s.Contains("cloth")
                    || s.Contains("clothes")
                    || s.Contains("wear")
                    || s.Contains("coordinate")
                    || s.Contains("custom")
                    || s.Contains("model")
                    || s.Contains("mesh")
                    || s.Contains("chara");
            }
            catch { return false; }
        }

        private static bool IsLikelyAssetPipelineMethod(MethodInfo method)
        {
            try
            {
                if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
                string name = method.Name ?? "";
                if (name.StartsWith("get_", StringComparison.Ordinal)
                    || name.StartsWith("set_", StringComparison.Ordinal)
                    || name.StartsWith("add_", StringComparison.Ordinal)
                    || name.StartsWith("remove_", StringComparison.Ordinal))
                    return false;
                if (name == "ToString" || name == "Equals" || name == "GetHashCode") return false;

                var ps = method.GetParameters();
                bool hasString = false;
                foreach (var p in ps)
                {
                    if (p == null) continue;
                    string pt = SafeTypeName(p.ParameterType).ToLowerInvariant();
                    if (pt == "string" || pt.EndsWith(".string") || pt.Contains("string"))
                    {
                        hasString = true;
                        break;
                    }
                }
                if (!hasString) return false;

                string m = name.ToLowerInvariant();
                bool action = m.Contains("load")
                    || m.Contains("create")
                    || m.Contains("read")
                    || m.Contains("open")
                    || m.Contains("import")
                    || m.Contains("set")
                    || m.Contains("change")
                    || m.Contains("replace")
                    || m.Contains("attach")
                    || m.Contains("wear")
                    || m.Contains("coordinate")
                    || m.Contains("body")
                    || m.Contains("cloth")
                    || m.Contains("mesh");
                if (!action) return false;

                string typeName = method.DeclaringType != null ? method.DeclaringType.FullName ?? "" : "";
                string all = (typeName + "." + name + " " + SafeTypeName(method.ReturnType)).ToLowerInvariant();
                return all.Contains("asset")
                    || all.Contains("bundle")
                    || all.Contains("resource")
                    || all.Contains("loader")
                    || all.Contains("prefab")
                    || all.Contains("character")
                    || all.Contains("human")
                    || all.Contains("body")
                    || all.Contains("cloth")
                    || all.Contains("clothes")
                    || all.Contains("wear")
                    || all.Contains("coordinate")
                    || all.Contains("custom")
                    || all.Contains("model")
                    || all.Contains("mesh")
                    || all.Contains("chara");
            }
            catch { return false; }
        }

        private static bool IsLikelyCustomShapeMethod(MethodInfo method)
        {
            try
            {
                if (method == null || method.IsAbstract || method.ContainsGenericParameters) return false;
                string name = method.Name ?? "";
                if (name.StartsWith("get_", StringComparison.Ordinal)) return false;
                if (name == "ToString" || name == "Equals" || name == "GetHashCode") return false;

                string m = name.ToLowerInvariant();
                bool strong = m.Contains("custom")
                    || m.Contains("shape")
                    || m.Contains("morph")
                    || m.Contains("blend")
                    || m.Contains("cloth")
                    || m.Contains("clothes")
                    || m.Contains("wear")
                    || m.Contains("coordinate")
                    || m.Contains("accessory");
                if (strong) return true;

                bool target = m.Contains("body") || m.Contains("mesh") || m.Contains("bone") || m.Contains("height") || m.Contains("bust") || m.Contains("waist");
                bool action = m.Contains("set") || m.Contains("apply") || m.Contains("load") || m.Contains("update") ||
                              m.Contains("change") || m.Contains("create") || m.Contains("init") || m.Contains("rebuild") ||
                              m.Contains("reflect") || m.Contains("replace") || m.Contains("copy");
                return target && action;
            }
            catch { return false; }
        }

        private static bool LooksInterestingCustomCall(object instance, object[] args, string methodName, string typeName)
        {
            try
            {
                if (LooksInteresting(methodName) || LooksInteresting(typeName)) return true;
                string m = (methodName ?? "").ToLowerInvariant();
                if (m.Contains("custom") || m.Contains("shape") || m.Contains("morph") || m.Contains("cloth") || m.Contains("mesh") || m.Contains("body")) return true;
                if (LooksInteresting(ObjectSummary(instance))) return true;
                if (args != null)
                    foreach (var arg in args)
                        if (LooksInteresting(ObjectSummary(arg)))
                            return true;
            }
            catch { }
            return false;
        }

        private static string ArgSummary(object[] args, int max)
        {
            try
            {
                if (args == null) return "[]";
                var parts = new List<string>();
                for (int i = 0; i < args.Length && i < max; i++)
                    parts.Add(ObjectSummary(args[i]));
                if (args.Length > max) parts.Add("...");
                return "[" + string.Join(", ", parts) + "]";
            }
            catch { return "[args-error]"; }
        }

        private static string ParamSignature(MethodInfo method)
        {
            try
            {
                var ps = method.GetParameters();
                var parts = new List<string>();
                foreach (var p in ps)
                    parts.Add(SafeTypeName(p.ParameterType) + " " + p.Name);
                return string.Join(", ", parts);
            }
            catch { return "params?"; }
        }

        private static string SafeTypeName(Type type)
        {
            try
            {
                if (type == null) return "null";
                return string.IsNullOrEmpty(type.FullName) ? type.Name : type.FullName;
            }
            catch { return "type?"; }
        }

        private static string ArgKey(object[] args)
        {
            try
            {
                if (args == null || args.Length == 0) return "argc=0";
                string firstInteresting = "";
                foreach (var arg in args)
                {
                    string s = ObjectSummary(arg);
                    if (LooksInteresting(s))
                    {
                        firstInteresting = s;
                        break;
                    }
                }
                if (firstInteresting.Length > 80) firstInteresting = firstInteresting.Substring(0, 80);
                return $"argc={args.Length}|{firstInteresting}";
            }
            catch { return "argc=?"; }
        }

        private static string ObjectSummary(object obj)
        {
            if (obj == null) return "null";
            try
            {
                if (obj is string s) return $"string:\"{Short(s, 96)}\"";
                if (obj is bool || obj is byte || obj is short || obj is int || obj is long ||
                    obj is float || obj is double || obj is decimal)
                    return obj.GetType().Name + ":" + obj;
                if (obj is Vector2 v2) return "Vector2:" + FormatSimpleValue(v2);
                if (obj is Vector3 v3) return "Vector3:" + Fmt(v3);
                if (obj is Vector4 v4) return "Vector4:" + FormatSimpleValue(v4);
                if (obj is Quaternion q) return "Quaternion:" + Fmt(q);

                if (obj is Mesh mesh) return "Mesh:" + MeshInfo(mesh) + " source=\"" + SourceOfMesh(mesh) + "\"";
                if (obj is SkinnedMeshRenderer smr) return "SMR:" + DescribeSMR(smr);
                if (obj is MeshFilter mf) return $"MeshFilter:\"{mf.name}\" mesh=\"{MeshInfo(mf.sharedMesh)}\" path=\"{PathOf(mf.transform)}\"";
                if (obj is Transform tf) return $"Transform:\"{tf.name}\" path=\"{PathOf(tf)}\"";
                if (obj is GameObject go) return $"GameObject:\"{go.name}\" path=\"{PathOf(go.transform)}\"";
                if (obj is Component c) return $"{c.GetType().Name}:\"{c.name}\" path=\"{PathOf(c.transform)}\"";
                if (obj is Human h) return $"Human:\"{h.name}\" hiPoly={h.hiPoly} path=\"{PathOf(h.transform)}\"";
                if (obj is UnityEngine.Object uo) return $"{uo.GetType().Name}:\"{uo.name}\" id={ObjId(uo)}";
                if (obj is Array arr) return $"{obj.GetType().Name}[{arr.Length}]";
                if (obj is System.Collections.ICollection col) return $"{obj.GetType().Name}[count={col.Count}]";

                var type = obj.GetType();
                var lenProp = type.GetProperty("Length") ?? type.GetProperty("Count");
                if (lenProp != null)
                {
                    object len = null;
                    try { len = lenProp.GetValue(obj, null); } catch { }
                    return $"{type.Name}[{len}]";
                }
                return type.Name;
            }
            catch (Exception e)
            {
                try { return obj.GetType().Name + "(summary-error:" + Short(e.Message, 60) + ")"; }
                catch { return "object(summary-error)"; }
            }
        }

        private static void ProbeSMRMeshAccess(string tag, SkinnedMeshRenderer smr)
        {
            try
            {
                var mesh = smr?.sharedMesh;
                if (mesh == null) return;
                if (!LooksInteresting(smr.name) && !LooksInteresting(mesh.name) && !LooksInteresting(PathOf(smr.transform))) return;

                string meshKey = ObjId(mesh) + "|" + mesh.name + "|" + mesh.vertexCount;
                lock (Sync)
                {
                    if (ProbedMeshes.Count >= 256) return;
                    if (!ProbedMeshes.Add(meshKey)) return;
                }

                bool readable = false;
                try { readable = mesh.isReadable; } catch { }

                string direct = readable ? ProbeDirectVertices(mesh) : "skip-unreadable";
                string clone = readable ? ProbeCloneVertices(mesh) : "skip-unreadable";
                string bake = ProbeBakeMesh(smr);
                string ro = ProbeAcquireReadOnlyMeshData(mesh);
                string boneWeights = ProbeBoneWeights(mesh);
                string bindposes = ProbeBindposes(mesh);
                string key = $"mesh-probe|{meshKey}";
                LogLimited(key, $"[MeshSpy] MeshAccessProbe tag=\"{Short(tag, 80)}\" smr=\"{smr.name}\" mesh=\"{MeshInfo(mesh)}\" direct={direct} clone={clone} bake={bake} readOnlyMeshData={ro} boneWeights={boneWeights} bindposes={bindposes} gpuPosition=disabled path=\"{PathOf(smr.transform)}\"", 1);
            }
            catch (Exception e)
            {
                LogLimited("mesh-probe-error", "[MeshSpy] MeshAccessProbe failed: " + e.Message, 4);
            }
        }

        private static string ProbeDirectVertices(Mesh mesh)
        {
            try
            {
                bool readable = false;
                try { readable = mesh.isReadable; } catch { }
                var verts = mesh.vertices;
                return $"ok readable={readable} len={verts?.Length ?? -1}";
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
        }

        private static string ProbeCloneVertices(Mesh mesh)
        {
            Mesh clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(mesh);
                if (clone == null) return "fail:null-clone";
                clone.name = mesh.name + "_spyClone";
                bool readable = false;
                try { readable = clone.isReadable; } catch { }
                string read = ProbeDirectVertices(clone);
                return $"cloneReadable={readable} {read}";
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
            finally
            {
                DestroyTempMesh(clone);
            }
        }

        private static string ProbeBoneWeights(Mesh mesh)
        {
            try
            {
                var bw = mesh.boneWeights;
                return "ok len=" + (bw?.Length ?? -1);
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
        }

        private static string ProbeBindposes(Mesh mesh)
        {
            try
            {
                var bindposes = mesh.bindposes;
                return "ok len=" + (bindposes?.Length ?? -1);
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
        }

        private static string ProbeBakeMesh(SkinnedMeshRenderer smr)
        {
            Mesh baked = null;
            try
            {
                baked = new Mesh();
                smr.BakeMesh(baked);
                bool readable = false;
                try { readable = baked.isReadable; } catch { }
                var verts = baked.vertices;
                return $"ok readable={readable} len={verts?.Length ?? -1} boundsC={Fmt(baked.bounds.center)} boundsS={Fmt(baked.bounds.size)}";
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
            finally
            {
                DestroyTempMesh(baked);
            }
        }

        private static string ProbeAcquireReadOnlyMeshData(Mesh mesh)
        {
            object meshDataArray = null;
            try
            {
                var method = GetAcquireReadOnlyMeshDataMethod();
                if (method == null) return "missing";
                meshDataArray = method.Invoke(null, new object[] { mesh });
                if (meshDataArray == null) return "null";

                string len = "?";
                try
                {
                    var prop = meshDataArray.GetType().GetProperty("Length") ?? meshDataArray.GetType().GetProperty("Count");
                    if (prop != null) len = Convert.ToString(prop.GetValue(meshDataArray, null));
                }
                catch { }
                return "ok len=" + len + " type=" + meshDataArray.GetType().Name;
            }
            catch (Exception e)
            {
                return "fail:" + Short(e.Message, 80);
            }
            finally
            {
                try
                {
                    if (meshDataArray is IDisposable disposable) disposable.Dispose();
                }
                catch { }
            }
        }

        private static MethodInfo GetAcquireReadOnlyMeshDataMethod()
        {
            if (_acquireReadOnlyMeshDataSearched) return _acquireReadOnlyMeshDataMethod;
            _acquireReadOnlyMeshDataSearched = true;
            try
            {
                foreach (var method in typeof(Mesh).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method == null || method.Name != "AcquireReadOnlyMeshData") continue;
                    var ps = method.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Mesh))
                    {
                        _acquireReadOnlyMeshDataMethod = method;
                        break;
                    }
                }
            }
            catch { }
            return _acquireReadOnlyMeshDataMethod;
        }

        private static void DestroyTempMesh(Mesh mesh)
        {
            try
            {
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
            catch { }
        }

        private static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static string FileInfoOf(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "file=unknown";
                var normalized = path.Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(normalized)) return "file=missing";
                var fi = new FileInfo(normalized);
                return $"fileSize={fi.Length} fileTime=\"{fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}\"";
            }
            catch { return "file=error"; }
        }

        private static bool LooksInterestingAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string s = path.ToLowerInvariant().Replace('\\', '/');
            return s.Contains("/chara/")
                || s.Contains("chara/")
                || s.Contains("/abdata/")
                || s.Contains("/abdata/chara")
                || s.Contains("/abdata/accessory")
                || s.Contains("/abdata/coordinate")
                || s.Contains("chara/accessory")
                || s.Contains("chara/acs")
                || s.EndsWith(".ab")
                || s.EndsWith(".bundle")
                || s.EndsWith(".assetbundle")
                || s.EndsWith(".assets")
                || s.EndsWith(".resource")
                || s.EndsWith(".ress")
                || s.EndsWith(".unity3d")
                || s.Contains("customshape")
                || s.Contains("body_00")
                || s.Contains("co_")
                || s.Contains("acs_")
                || s.Contains("accessory")
                || s.Contains("mt_body")
                || s.Contains("cf_anmshape")
                || s.Contains("cf_custombody")
                || LooksInteresting(s);
        }

        private static bool LooksCharacterMeshAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string s = path.ToLowerInvariant().Replace('\\', '/');
            return s.Contains("/abdata/chara/")
                || s.Contains("abdata/chara/")
                || s.Contains("chara/body/")
                || s.Contains("chara/co_")
                || s.Contains("chara/accessory")
                || s.Contains("chara/acs")
                || s.Contains("customshape")
                || s.Contains("body_00")
                || s.Contains("cf_anmshape")
                || s.Contains("cf_custombody");
        }

        private static bool LogGameObject(string kind, string context, GameObject go, int limit)
        {
            if (go == null) return false;
            try
            {
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                bool hasMesh = (smrs != null && smrs.Length > 0) || (mfs != null && mfs.Length > 0);
                if (!hasMesh) return false;

                string key = $"{kind}|go|{context}|{go.name}|{smrs?.Length ?? 0}|{mfs?.Length ?? 0}";
                if (!ShouldLog(key, limit)) return true;

                LogPlain($"[MeshSpy] {kind} {context} object=GameObject go=\"{go.name}\" path=\"{PathOf(go.transform)}\" active={go.activeSelf}/{go.activeInHierarchy} smrs={smrs?.Length ?? 0} meshFilters={mfs?.Length ?? 0}");
                int shown = 0;
                if (smrs != null)
                {
                    foreach (var smr in smrs)
                    {
                        if (smr?.sharedMesh == null) continue;
                        RememberMeshSource(smr.sharedMesh, $"{kind} {context} go=\"{go.name}\" smr=\"{smr.name}\"");
                        LogPlain("[MeshSpy]  assetSMR " + DescribeSMR(smr));
                        ProbeSMRMeshAccess($"{kind}:{go.name}:{smr.name}", smr);
                        LogSMRWeightStats($"{kind}:{go.name}:{smr.name}", smr, smr.sharedMesh);
                        if (++shown >= 128) break;
                    }
                }
                shown = 0;
                if (mfs != null)
                {
                    foreach (var mf in mfs)
                    {
                        if (mf?.sharedMesh == null) continue;
                        RememberMeshSource(mf.sharedMesh, $"{kind} {context} go=\"{go.name}\" meshFilter=\"{mf.name}\"");
                        LogPlain($"[MeshSpy]  assetMF name=\"{mf.name}\" mesh=\"{MeshInfo(mf.sharedMesh)}\" path=\"{PathOf(mf.transform)}\"");
                        if (++shown >= 64) break;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                LogLimited($"{kind}|go-error|{go?.name}", $"[MeshSpy] {kind} GameObject inspect failed go=\"{go?.name}\" {e.Message}", 2);
                return false;
            }
        }

        private static void LogInterestingSMR(string kind, SkinnedMeshRenderer smr, Mesh mesh, int limit)
        {
            if (smr == null || mesh == null) return;
            string key = $"{kind}|{PathOf(smr.transform)}|{mesh.name}|{mesh.vertexCount}";
            LogLimited(key, $"[MeshSpy] {kind} {DescribeSMR(smr)}", limit);
            ProbeSMRMeshAccess(kind, smr);
            LogSMRWeightStats(kind, smr, mesh);
        }

        private static void LogSMRWeightStats(string tag, SkinnedMeshRenderer smr, Mesh mesh)
        {
            try
            {
                if (smr == null || mesh == null) return;
                if (!LooksInterestingSMR(smr, mesh) && !HasRelevantBones(smr)) return;

                string path = PathOf(smr.transform);
                string boneSig = RelevantBoneSignature(smr, 20);
                string probeKey = $"{ObjId(mesh)}|{mesh.name}|{mesh.vertexCount}|{path}|{boneSig}";
                lock (Sync)
                {
                    if (ProbedWeightStats.Count >= 512) return;
                    if (!ProbedWeightStats.Add(probeKey)) return;
                }

                string root = "null";
                try { root = smr.rootBone != null ? smr.rootBone.name : "null"; } catch { }
                string route = ClassifyMountedRoute(smr, mesh, root, out int bellyBones, out int legBones, out int breastBones, out string relBones);
                string stats = BuildWeightStats(smr, mesh);
                LogLimited(
                    $"weight-stats|{probeKey}",
                    $"[MeshSpy] WeightStats tag=\"{Short(tag, 80)}\" route={route} smr=\"{smr.name}\" mesh=\"{MeshInfo(mesh)}\" rootBone=\"{root}\" boneClassCounts=belly:{bellyBones},leg:{legBones},breast:{breastBones} relBones=\"{relBones}\" {stats} path=\"{path}\" boneSig=\"{boneSig}\"",
                    1);
            }
            catch (Exception e)
            {
                LogLimited("weight-stats-error", "[MeshSpy] WeightStats error: " + Short(e.Message, 160), 8);
            }
        }

        private static string BuildWeightStats(SkinnedMeshRenderer smr, Mesh mesh)
        {
            try
            {
                var bones = smr?.bones;
                int boneCount = bones?.Length ?? 0;
                int vertexCount = -1;
                try { vertexCount = mesh.vertexCount; } catch { }
                if (boneCount == 0) return $"weightStats=no-bones verts={vertexCount}";

                bool[] currentBelly = new bool[boneCount];
                bool[] currentLeg = new bool[boneCount];
                bool[] coreBelly = new bool[boneCount];
                bool[] hipEdge = new bool[boneCount];
                bool[] strongLeg = new bool[boneCount];
                bool[] breast = new bool[boneCount];

                for (int i = 0; i < boneCount; i++)
                {
                    string name = bones[i]?.name ?? "";
                    currentBelly[i] = IsBellyOrWaistBoneName(name);
                    currentLeg[i] = IsLegBoneName(name);
                    hipEdge[i] = IsHipEdgeBoneName(name);
                    strongLeg[i] = IsStrongLegBoneName(name);
                    coreBelly[i] = IsCoreBellyBoneNameForStats(name);
                    breast[i] = IsBreastBoneName(name);
                }

                var bw = mesh.boneWeights;
                int bwLen = bw?.Length ?? -1;
                if (bw == null || bwLen == 0)
                    return $"weightStats=no-boneWeights verts={vertexCount} bw={bwLen} idx={WeightClassCounts(currentBelly, currentLeg, coreBelly, hipEdge, strongLeg, breast)}";

                int sample = vertexCount > 0 ? Math.Min(vertexCount, bwLen) : bwLen;
                int currentPass = 0;
                int currentBlocked = 0;
                int corePass = 0;
                int coreHipPass = 0;
                int hipOnly = 0;
                int strongLegBlocks = 0;
                int breastPass = 0;
                float sumCurrentBelly = 0f, sumCurrentLeg = 0f, sumCore = 0f, sumHip = 0f, sumStrong = 0f, sumBreast = 0f;
                float maxCurrentBelly = 0f, maxCurrentLeg = 0f, maxCore = 0f, maxHip = 0f, maxStrong = 0f, maxBreast = 0f;

                for (int i = 0; i < sample; i++)
                {
                    BoneWeight w = bw[i];
                    float curBellyW = WeightFor(w, currentBelly);
                    float curLegW = WeightFor(w, currentLeg);
                    float coreW = WeightFor(w, coreBelly);
                    float hipW = WeightFor(w, hipEdge);
                    float strongW = WeightFor(w, strongLeg);
                    float breastW = WeightFor(w, breast);
                    float coreHipW = coreW + hipW;

                    sumCurrentBelly += curBellyW;
                    sumCurrentLeg += curLegW;
                    sumCore += coreW;
                    sumHip += hipW;
                    sumStrong += strongW;
                    sumBreast += breastW;
                    if (curBellyW > maxCurrentBelly) maxCurrentBelly = curBellyW;
                    if (curLegW > maxCurrentLeg) maxCurrentLeg = curLegW;
                    if (coreW > maxCore) maxCore = coreW;
                    if (hipW > maxHip) maxHip = hipW;
                    if (strongW > maxStrong) maxStrong = strongW;
                    if (breastW > maxBreast) maxBreast = breastW;

                    if (curBellyW >= 0.02f)
                    {
                        if (curLegW > curBellyW) currentBlocked++;
                        else currentPass++;
                    }
                    if (coreW >= 0.02f) corePass++;
                    if (coreHipW >= 0.02f) coreHipPass++;
                    if (coreW < 0.02f && coreHipW >= 0.02f) hipOnly++;
                    if (coreHipW >= 0.02f && strongW > coreHipW) strongLegBlocks++;
                    if (breastW >= 0.02f) breastPass++;
                }

                string mismatch = vertexCount >= 0 && vertexCount != bwLen ? $" mismatch=verts:{vertexCount}/bw:{bwLen}" : "";
                return
                    $"weightStats=ok verts={vertexCount} bw={bwLen} sample={sample}{mismatch} " +
                    $"currentMaskPass={CountPct(currentPass, sample)} currentLegBlocked={CountPct(currentBlocked, sample)} " +
                    $"coreBellyPass={CountPct(corePass, sample)} corePlusHipEdgePass={CountPct(coreHipPass, sample)} hipEdgeOnly={CountPct(hipOnly, sample)} strongLegBlocks={CountPct(strongLegBlocks, sample)} breastPass={CountPct(breastPass, sample)} " +
                    $"avgW=currentBelly:{Avg(sumCurrentBelly, sample)},currentLeg:{Avg(sumCurrentLeg, sample)},core:{Avg(sumCore, sample)},hipEdge:{Avg(sumHip, sample)},strongLeg:{Avg(sumStrong, sample)},breast:{Avg(sumBreast, sample)} " +
                    $"maxW=currentBelly:{maxCurrentBelly:F3},currentLeg:{maxCurrentLeg:F3},core:{maxCore:F3},hipEdge:{maxHip:F3},strongLeg:{maxStrong:F3},breast:{maxBreast:F3} " +
                    $"idx={WeightClassCounts(currentBelly, currentLeg, coreBelly, hipEdge, strongLeg, breast)} " +
                    $"names=\"currentBelly=[{FlaggedBoneNames(bones, currentBelly, 8)}] currentLeg=[{FlaggedBoneNames(bones, currentLeg, 8)}] core=[{FlaggedBoneNames(bones, coreBelly, 8)}] hipEdge=[{FlaggedBoneNames(bones, hipEdge, 8)}] strongLeg=[{FlaggedBoneNames(bones, strongLeg, 8)}] breast=[{FlaggedBoneNames(bones, breast, 8)}]\"";
            }
            catch (Exception e)
            {
                return "weightStats=fail:" + Short(e.Message, 160);
            }
        }

        private static float WeightFor(BoneWeight w, bool[] flags)
        {
            float sum = 0f;
            if (HasFlag(flags, w.boneIndex0)) sum += w.weight0;
            if (HasFlag(flags, w.boneIndex1)) sum += w.weight1;
            if (HasFlag(flags, w.boneIndex2)) sum += w.weight2;
            if (HasFlag(flags, w.boneIndex3)) sum += w.weight3;
            return sum;
        }

        private static bool HasFlag(bool[] flags, int index)
        {
            return flags != null && index >= 0 && index < flags.Length && flags[index];
        }

        private static string CountPct(int count, int total)
        {
            if (total <= 0) return count + "/0";
            return $"{count}/{total}({(count * 100f / total):F1}%)";
        }

        private static string Avg(float sum, int total)
        {
            return total > 0 ? (sum / total).ToString("F3") : "0.000";
        }

        private static string WeightClassCounts(bool[] currentBelly, bool[] currentLeg, bool[] coreBelly, bool[] hipEdge, bool[] strongLeg, bool[] breast)
        {
            return $"currentBelly:{CountFlags(currentBelly)},currentLeg:{CountFlags(currentLeg)},core:{CountFlags(coreBelly)},hipEdge:{CountFlags(hipEdge)},strongLeg:{CountFlags(strongLeg)},breast:{CountFlags(breast)}";
        }

        private static int CountFlags(bool[] flags)
        {
            if (flags == null) return 0;
            int count = 0;
            for (int i = 0; i < flags.Length; i++)
                if (flags[i]) count++;
            return count;
        }

        private static string FlaggedBoneNames(IList<Transform> bones, bool[] flags, int max)
        {
            try
            {
                if (bones == null || flags == null) return "";
                var names = new List<string>();
                int count = Math.Min(bones.Count, flags.Length);
                for (int i = 0; i < count; i++)
                {
                    if (!flags[i]) continue;
                    names.Add(i + ":" + (bones[i]?.name ?? "null"));
                    if (names.Count >= max) break;
                }
                if (CountFlags(flags) > names.Count) names.Add("...");
                return string.Join(",", names);
            }
            catch { return "bone-name-error"; }
        }

        private static string RelevantBoneSignature(SkinnedMeshRenderer smr, int max)
        {
            try
            {
                var bones = smr?.bones;
                if (bones == null || bones.Length == 0) return "";
                var shown = new List<string>();
                for (int i = 0; i < bones.Length; i++)
                {
                    string name = bones[i]?.name ?? "";
                    if (!IsBellyOrWaistBoneName(name) && !IsLegBoneName(name) && !IsBreastBoneName(name) && !IsHipEdgeBoneName(name)) continue;
                    shown.Add(i + ":" + name);
                    if (shown.Count >= max) break;
                }
                return string.Join(",", shown);
            }
            catch { return "bone-sig-error"; }
        }

        private static bool LooksInterestingSMR(SkinnedMeshRenderer smr, Mesh mesh)
        {
            try
            {
                if (smr == null) return false;
                string path = PathOf(smr.transform);
                string id = ((smr.name ?? "") + "/" + (mesh?.name ?? "") + "/" + path).ToLowerInvariant();
                return LooksInteresting(id)
                    || LooksClothLikeId(id)
                    || LooksAccessoryLikeId(id)
                    || PathHasBellyAnchor(id)
                    || HasRelevantBones(smr);
            }
            catch { return false; }
        }

        private static string DescribeSMR(SkinnedMeshRenderer smr)
        {
            if (smr == null) return "smr=null";
            Mesh mesh = null;
            try { mesh = smr.sharedMesh; } catch { }
            int bones = -1, bindposes = -1;
            string root = "null";
            bool activeSelf = false, activeHierarchy = false, enabled = false, update = false;
            Bounds lb = default;
            try { bones = smr.bones?.Length ?? 0; } catch { }
            try { bindposes = mesh?.bindposes?.Length ?? 0; } catch { }
            try { root = smr.rootBone != null ? smr.rootBone.name : "null"; } catch { }
            try { activeSelf = smr.gameObject.activeSelf; activeHierarchy = smr.gameObject.activeInHierarchy; } catch { }
            try { enabled = smr.enabled; } catch { }
            try { update = smr.updateWhenOffscreen; } catch { }
            try { lb = smr.localBounds; } catch { }
            string source = SourceOfMesh(mesh);
            string sourcePart = string.IsNullOrEmpty(source) ? "" : $" meshSource=\"{source}\"";
            string route = ClassifyMountedRoute(smr, mesh, root, out int bellyBones, out int legBones, out int breastBones, out string relBones);
            return $"route={route} smr=\"{smr.name}\" mesh=\"{MeshInfo(mesh)}\"{sourcePart} bones={bones} bindposes={bindposes} bellyBones={bellyBones} legBones={legBones} breastBones={breastBones} rootBone=\"{root}\" relBones=\"{relBones}\" enabled={enabled} active={activeSelf}/{activeHierarchy} updateWhenOffscreen={update} localBoundsC={Fmt(lb.center)} localBoundsS={Fmt(lb.size)} path=\"{PathOf(smr.transform)}\"";
        }

        private static string ClassifyMountedRoute(
            SkinnedMeshRenderer smr,
            Mesh mesh,
            string root,
            out int bellyBones,
            out int legBones,
            out int breastBones,
            out string relBones)
        {
            bellyBones = 0;
            legBones = 0;
            breastBones = 0;
            relBones = "";
            try
            {
                string path = PathOf(smr?.transform);
                string id = ((smr?.name ?? "") + "/" + (mesh?.name ?? "") + "/" + path).ToLowerInvariant();
                relBones = SummarizeRelevantBones(smr, out bellyBones, out legBones, out breastBones);

                bool cloth = LooksClothLikeId(id);
                bool accessory = LooksAccessoryLikeId(id);
                bool belly = bellyBones > 0 || IsBellyOrWaistBoneName(root) || PathHasBellyAnchor(id);

                if (cloth) return "cloth:" + ClassifyClothLikeId(id);
                if (accessory && belly) return "accessory-belly";
                if (accessory) return "accessory-other";
                if (belly) return "belly-bone-unknown";
                return "other";
            }
            catch
            {
                relBones = "route-error";
                return "error";
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

        private static bool HasRelevantBones(SkinnedMeshRenderer smr)
        {
            try
            {
                SummarizeRelevantBones(smr, out int belly, out int leg, out int breast);
                return belly > 0 || leg > 0 || breast > 0;
            }
            catch { return false; }
        }

        private static string MeshInfo(Mesh mesh)
        {
            if (mesh == null) return "null";
            bool readable = false;
            int verts = -1, blend = -1, sub = -1;
            try { readable = mesh.isReadable; } catch { }
            try { verts = mesh.vertexCount; } catch { }
            try { blend = mesh.blendShapeCount; } catch { }
            try { sub = mesh.subMeshCount; } catch { }
            return $"{mesh.name} verts={verts} readable={readable} sub={sub} blend={blend}";
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

        private static bool LooksInteresting(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string s = name.ToLowerInvariant();
            return s.Contains("body")
                || s.Contains("belly")
                || s.Contains("hara")
                || s.Contains("cloth")
                || s.Contains("clothes")
                || s.Contains("chara/co_")
                || s.Contains("p_cf_")
                || s.Contains("p_cm_")
                || s.Contains("o_top")
                || s.Contains("o_bot")
                || s.Contains("o_bra")
                || s.Contains("o_shorts")
                || s.Contains("o_panst")
                || s.Contains("panst")
                || s.Contains("o_socks")
                || s.Contains("o_sock")
                || s.Contains("o_shocks")
                || s.Contains("socks")
                || s.Contains("sock")
                || s.Contains("shocks")
                || s.Contains("tights")
                || s.Contains("stocking")
                || s.Contains("garter")
                || s.Contains("sailor")
                || s.Contains("skirt")
                || s.Contains("shirt")
                || s.Contains("onep")
                || s.Contains("onepiece")
                || s.Contains("bra")
                || s.Contains("shorts")
                || s.Contains("swim")
                || s.Contains("mizugi")
                || s.Contains("bikini")
                || s.Contains("leotard")
                || s.Contains("inner")
                || s.Contains("underwear")
                || s.Contains("fincent")
                || s.Contains("accessory")
                || s.Contains("accessories")
                || s.Contains("accessary")
                || s.Contains("o_acs")
                || s.Contains("n_acs")
                || s.Contains("_acs")
                || s.Contains("/acs")
                || s.Contains("acs_")
                || s.Contains("acc_")
                || s.Contains("belt")
                || s.Contains("waist")
                || s.Contains("navel")
                || s.Contains("pierce")
                || s.Contains("ribbon")
                || s.Contains("chain")
                || s.Contains("charm")
                || s.Contains("n_body")
                || s == "o_body";
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
                   id.Contains("garter") || id.Contains("swim") || id.Contains("mizugi") ||
                   id.Contains("bikini") || id.Contains("leotard") || id.Contains("inner") ||
                   id.Contains("underwear") || id.Contains("pants") || id.Contains("shirt") ||
                   id.Contains("camisole") || id.Contains("fincent") || id.Contains("sailor") ||
                   id.Contains("/co_") || id.Contains("\\co_") || id.Contains("costume");
        }

        private static string ClassifyClothLikeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "other";
            if (id.Contains("panst") || id.Contains("socks") || id.Contains("sock") ||
                id.Contains("shocks") || id.Contains("tights") ||
                id.Contains("stocking") || id.Contains("stockings") || id.Contains("garter"))
                return "panst";
            if (id.Contains("shorts")) return "shorts";
            if (id.Contains("bra") || id.Contains("swim") || id.Contains("mizugi") ||
                id.Contains("bikini") || id.Contains("leotard"))
                return "bra";
            if (id.Contains("onep") || id.Contains("onepiece") ||
                id.Contains("o_top") || id.Contains("/top/") || id.Contains("n_top") ||
                id.Contains("shirt") || id.Contains("camisole") ||
                id.Contains("fincent") || id.Contains("sailor"))
                return "top";
            if (id.Contains("o_bot") || id.Contains("/bot/") || id.Contains("n_bot") ||
                id.Contains("skirt") || id.Contains("pants"))
                return "bottom";
            return "other";
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
            return lower.Contains("thigh") ||
                   (lower.Contains("leg") && !lower.Contains("spine")) ||
                   lower.Contains("knee");
        }

        private static bool IsHipEdgeBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("hipleg") ||
                   lower.Contains("hip_leg") ||
                   lower.Contains("hip-leg") ||
                   lower.Contains("hip leg");
        }

        private static bool IsStrongLegBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            if (IsHipEdgeBoneName(lower)) return false;
            return lower.Contains("thigh") ||
                   lower.Contains("knee") ||
                   (lower.Contains("leg") && !lower.Contains("spine"));
        }

        private static bool IsCoreBellyBoneNameForStats(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IsHipEdgeBoneName(name) || IsStrongLegBoneName(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("waist") ||
                   lower.Contains("spine") ||
                   lower.Contains("belly") ||
                   lower.Contains("pelvis") ||
                   lower.Contains("hip") ||
                   lower.Contains("hips");
        }

        private static bool IsBreastBoneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("mune")
                || lower.Contains("breast")
                || lower.Contains("bust")
                || lower.Contains("chichi")
                || lower.Contains("chikubi")
                || lower.Contains("nipple");
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

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F3},{v.y:F3},{v.z:F3})";
        }

        private static string Fmt(Quaternion q)
        {
            return $"({q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3})";
        }

        private static bool ShouldLog(string key, int limit)
        {
            if (!SpyLogEnabled) return false;
            lock (Sync)
            {
                if (_totalLogs >= MaxTotalLogs)
                {
                    if (!_limitLogged)
                    {
                        _limitLogged = true;
                        Log.LogWarning($"[MeshSpy] log limit reached ({MaxTotalLogs}); further spy logs suppressed");
                    }
                    return false;
                }

                if (!Seen.TryGetValue(key, out int count)) count = 0;
                if (count >= limit) return false;
                Seen[key] = count + 1;
                _totalLogs++;
                return true;
            }
        }

        private static void LogLimited(string key, string message, int limit)
        {
            if (ShouldLog(key, limit))
                LogPlain(message);
        }

        private static void LogPlain(string message)
        {
            if (!SpyLogEnabled) return;
            try { Log.LogInfo(message); } catch { }
        }
    }
}

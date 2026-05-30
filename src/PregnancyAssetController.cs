using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Unity.Component;
using Manager;
using SV.H;
using System;
//using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Il2CppInterop.Common.Attributes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
//using Il2CppSystem;
//using Il2CppSystem.Collections.Generic;
//using Il2CppSystem.Reflection;
using Il2CppSystem.Runtime.CompilerServices;
using Il2CppSystem.Threading;
using ILLGames.Unity;
using System.Threading.Tasks;
using ADV;
using SaveData;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using ADV.Commands.Object;
using CharacterCreation.UI.View;
using SV.H.Motion.State;
using ILLGames.Extensions;
using UnityEngine.PlayerLoop;
using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace SVSPregnancy
{
    // Token: 0x02000004 RID: 4
    public class PregnancyAssetController : MonoBehaviour
    {

        public static new PregnancyAssetController _instance { get; protected set; }
        public bool _inited = false;
        public static Il2CppSystem.Collections.Generic.Dictionary<int, Actor> _Charas => Manager.Game.Charas;
        public delegate void StartContinueDelegate();

        public static IntPtr audioObjectPtr = IntPtr.Zero;
        public static GameObject audioObject => audioObjectPtr.ToObject<GameObject>();

        public static IntPtr CutInFertilize_PlayerPtr { get; internal set; }
        public static AudioSource CutInFertilize_Player => CutInFertilize_PlayerPtr.ToObject<AudioSource>();

        public static IntPtr CutInFertilize_SoundPtr;
        internal static AudioClip CutInFertilize_Sound => CutInFertilize_SoundPtr.ToObject<AudioClip>();
        public PregnancyAssetController(IntPtr ptr) : base(ptr)
        {
            _instance = this;

        }

        public void Init()
        {
            
            var audioObject = new GameObject("AudioObject");
            UnityEngine.Object.DontDestroyOnLoad(audioObject);
            audioObjectPtr = audioObject.ToPtr();
            CutInFertilize_PlayerPtr = audioObject.GetOrAddComponent<AudioSource>().Cast<AudioSource>().ToPtr();
            LoadAudioClip(PregnancyPlugin.CutInFertilize_SoundPath);
            _inited=true;

        }


        
        IEnumerator LoadAudioClipFromURL(string url)
        {
            // Create a UnityWebRequest to load the audio file
            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN); // Use AudioType for .mp3, .ogg, etc.

            // Send the request and wait for the response
            yield return www.SendWebRequest();

            // Ensure proper cleanup after the request (dispose manually)
            

            // Check if the request was successful
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading audio: " + www.error);
            }
            else
            {
                // Successfully received the audio data
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                UnityEngine.Object.DontDestroyOnLoad(clip);
                CutInFertilize_Player.clip = clip;
                CutInFertilize_SoundPtr = clip.ToPtr();
            }
            www.Dispose();
        }
        internal bool LoadAudioClip(string filePath)
        {
            // Audio loading disabled — DownloadHandlerAudioClip API is incompatible with this game version
            return false;
        }
        List<Texture2D> testvarible => TextureLoader.FertilizeCutInTextures;//.Select(p => p.ToObject<Texture2D>()).ToList();
        

        internal static class TextureLoader
        {


            
            private static List<IntPtr> GetTextures(List<string> fileNames)
            {
                // List<string> fileNames = fileNamesPtr.ToObject<List<string>>();
                string lewdCrestDirectory = (PregnancyPlugin.TextureFolder.Contains(":") ? PregnancyPlugin.TextureFolder : Path.Combine(Paths.GameRootPath, PregnancyPlugin.TextureFolder));
                if (PregnancyPlugin.TextureFormat == null || PregnancyPlugin.TextureFormat.Value == PregnancyPlugin.DefaultTextureFormat.dxt5)
                {

                    var list = (from bytes in fileNames.Select(ReadFile)
                                select bytes.ToArray().LoadTexture(TextureFormat.DXT5).ToPtr()).ToList();
                    foreach (var t in list)
                    {
                        Texture2D tex = t.ToObject<Texture2D>();
                        UnityEngine.Object.DontDestroyOnLoad(tex);
                    }

                    return list;//List<Texture2D*>
                }
                else
                {
                    var list = (from bytes in fileNames.Select(ReadFile)
                                select bytes.ToArray().LoadTexture(TextureFormat.BC7).ToPtr()).ToList();
                    return list;
                }
                List<byte> ReadFile(string fileName)
                {
                    using FileStream fileStream = new FileStream(Path.Combine(lewdCrestDirectory, fileName), FileMode.Open);
                    return (fileStream ?? throw new InvalidOperationException("The resource " + fileName + " was not found")).ReadAllBytes();
                }
            }



            static TextureLoader()
            {
                TextureLoader.RefreshTextures(false);
            }

            public static void RefreshTextures(bool dispose)
            {
                if (dispose)
                {

                    if (TextureLoader._fertilizeCutInResources != null)
                    {
                        TextureLoader._fertilizeCutInTextures.ToList<Texture2D>().ForEach(delegate (Texture2D texture)
                        {
                            UnityEngine.Object.Destroy(texture);
                        });
                        TextureLoader._fertilizeCutInTexturePtrs = null;
                    }

                }
                string path = PregnancyPlugin.TextureFolder.Contains(":") ? PregnancyPlugin.TextureFolder : Path.Combine(Paths.GameRootPath, PregnancyPlugin.TextureFolder);
                List<string> list = new List<string>();
                if (Directory.Exists(path))
                {
                    foreach (string path2 in Directory.GetFiles(path, "*.png"))
                    {

                        list.Add(Path.GetFileName(path2));
                    }
                }
                TextureLoader._fertilizeCutInResources = (from x in list
                                                          where x.IndexOf("CTFertilize", StringComparison.OrdinalIgnoreCase) >= 0
                                                          select x).ToList();
                TextureLoader._fertilizeCutInResources.Sort();

            }


            public static int FertilizeCutInTexturesCount
            {
                get
                {
                    return TextureLoader._fertilizeCutInResources.Count;
                }
            }


             public static List<Texture2D> FertilizeCutInTextures => FertilizeCutInTexturePtrs.Select(p => p.ToObject<Texture2D>()).ToList();
            public static List<IntPtr> FertilizeCutInTexturePtrs
            {
                get
                {

                    if (TextureLoader._fertilizeCutInTexturePtrs == null)
                    {

                        TextureLoader._fertilizeCutInTexturePtrs = TextureLoader.GetTextures(TextureLoader._fertilizeCutInResources);

                    }
                    // TextureLoader._fertilizeCutInTexturePtrs[0].ToLog();
                    return TextureLoader._fertilizeCutInTexturePtrs;
                }
            }

            public static void PreloadAllTextures()
            {
                PregnancyPlugin._assetController.StartCoroutine(TextureLoader._PreloadAllTextures().WrapToIl2Cpp());

            }


            private static IEnumerator _PreloadAllTextures()
            {

                List<Texture2D> fertilizeCutInTextures = TextureLoader.FertilizeCutInTextures;
                yield break;
            }
            
            private static List<string> _fertilizeCutInResources;

            private static List<IntPtr> _fertilizeCutInTexturePtrs;


            private static List<Texture2D> _fertilizeCutInTextures=> _fertilizeCutInTexturePtrs.Select(p=>p.ToObject<Texture2D>()).ToList();
        }


        public class Wipe
        {
            public enum CutInMode
            {
                None,
                Right2Left,
                Left2Right,
                Top2Down,
                Down2Top,
                Back2Front,
                Front2Back
            }

            public class cutIn
            {
                public CutInMode mode;

                public float framesPerSecond = 10f;

                public IntPtr videoPtr;
                public RawImage video => videoPtr.ToObject<RawImage>();

                public bool loop = true;

                public int frameIndex;

                public bool cancel;

                internal float smoothTime = 0.3f;

                internal Vector3 velocity = Vector3.zero;

                internal Vector3 velocity2 = Vector3.zero;

                internal Vector3 targetPosition = Vector3.zero;

                internal Vector3 targetScale = Vector3.one;

                internal float targetAlpha;

                internal float velocityAlpha;

                internal float secondTimer;

                internal List<IntPtr> framePtrs =null;
                internal List<Texture2D> frames=> framePtrs.Select(p => p.ToObject<Texture2D>()).ToList();


                internal float hoseiX;

                internal float hoseiY;

                internal float hoseiZ;

                internal float waitTimer;

                internal float _alpha;

                public float Width
                {
                    get
                    {
                        if (video == null || video.texture == null)
                        {
                            return 0f;
                        }

                        return video.texture.width;
                    }
                }

                public float Height
                {
                    get
                    {
                        if (video == null || video.texture == null)
                        {
                            return 0f;
                        }

                        return video.texture.height;
                    }
                }

                public Transform Transform => video?.transform;

                public float alpha
                {
                    get
                    {
                        return _alpha;
                    }
                    set
                    {
                        _alpha = value;
                        if (video != null && video.texture != null)
                        {
                            video.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_alpha));
                        }
                    }
                }
            }

            internal static DefaultControls.Resources resources = new UnityEngine.UI.DefaultControls.Resources();

            internal static IntPtr PanePtr;
            internal static GameObject Pane => PanePtr.ToObject<GameObject>();

            public static cutIn DisplayCutIn(CutInMode mode, List<IntPtr> framePtrs, float firstWait, float secondTimer, float hoseiX, float hoseiY, float hoseiZ, float ratio = 1f)
            {

                if (Pane == null)
                {
                    PanePtr = new GameObject("SVS_LewdCrest_Wipe").ToPtr();
                }

                CanvasScaler obj = Pane.GetOrAddComponent<CanvasScaler>();
                obj.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                obj.referenceResolution = new Vector2(Screen.width, Screen.height);
                obj.matchWidthOrHeight = 0.5f;
                Canvas canvas = Pane.GetOrAddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -1;
                (Pane.GetOrAddComponent<CanvasGroup>()).blocksRaycasts = false;
                GameObject gameObject = new GameObject();
                gameObject.transform.SetParent(canvas.transform, worldPositionStays: false);
                gameObject.SetActive(value: false);
                cutIn cutIn = new cutIn();
                cutIn.mode = mode;
                cutIn.secondTimer = secondTimer;
                cutIn.framePtrs = framePtrs;
                cutIn.videoPtr = CreateRawImage("", gameObject.transform.ToPtr(), IntPtr.Zero);
                cutIn.hoseiZ = hoseiZ / 100f;
                cutIn.video.rectTransform.sizeDelta = new Vector2((float)Screen.height * ratio * cutIn.hoseiZ, (float)Screen.height * cutIn.hoseiZ);
                cutIn.hoseiX = (float)Screen.width / 2f * (hoseiX / 100f);
                cutIn.hoseiY = (float)Screen.height / 2f * (hoseiY / 100f);
                cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                cutIn.Transform.position = cutIn.targetPosition;
                cutIn.alpha = 0f;
                float t0 = Time.time;
                PregnancyPlugin._assetController.StartCoroutine(_DisplayCutIn(gameObject, cutIn, firstWait, t0).WrapToIl2Cpp());

                //_DisplayCutIn(gameObject, cutIn, firstWait, t0);

                return cutIn;
                static IntPtr CreateRawImage(string objectName, IntPtr pp, IntPtr texturep)//RawImage*
                {
                    Texture texture = texturep.ToObject<Texture>();
                    Transform p = pp.ToObject<Transform>();
                    GameObject gameObject2 = DefaultControls.CreateRawImage(resources);
                    gameObject2.name = objectName;
                    gameObject2.transform.SetParent(p, worldPositionStays: false);
                    RawImage component = gameObject2.GetOrAddComponent<RawImage>();
                    if (texture != null)
                    {
                        component.texture = texture;
                    }
                    return component.ToPtr();
                }
            }

            internal static IEnumerator _DisplayCutIn(GameObject gameObject, cutIn cutIn, float firstWait, float t0)
            {

                yield return new WaitForSeconds(firstWait);
                if (cutIn.mode == CutInMode.None)
                {
                    cutIn.alpha = 1f;
                    cutIn.waitTimer = cutIn.secondTimer;
                }
                else
                {
                    firstMode(cutIn);
                    cutIn.waitTimer = 0f;
                }

                gameObject.SetActive(value: true);
                bool b = true;
                Il2CppSystem.Object? nullobj = null;
                while (b)
                {



                    if (cutIn.frames != null && cutIn.frames.Count != 0)
                    {

                        if (cutIn.loop)
                        {

                            int num = (int)((Time.time - firstWait - t0) * cutIn.framesPerSecond);
                            cutIn.frameIndex = num % cutIn.frames.Count;
                        }

                        if (cutIn.frameIndex < cutIn.frames.Count)
                        {

                            cutIn.video.texture = cutIn.frames[cutIn.frameIndex];
                        }

                    }

                    if (!cutIn.cancel && cutIn.waitTimer > 0f)
                    {
                        cutIn.waitTimer -= Time.deltaTime;

                    }
                    else
                    {
                        if (cutIn.cancel || cutIn.mode == CutInMode.None)
                        {

                            if (null != cutIn.Transform.parent.gameObject)
                            {
                                UnityEngine.Object.Destroy(cutIn.Transform.parent.gameObject);
                                cutIn.videoPtr = IntPtr.Zero;

                            }
                            b = false;
                            yield break;
                        }

                        cutIn.alpha = Mathf.SmoothDamp(cutIn.alpha, cutIn.targetAlpha, ref cutIn.velocityAlpha, cutIn.smoothTime);
                        cutIn.Transform.localScale = Vector3.SmoothDamp(cutIn.Transform.localScale, cutIn.targetScale, ref cutIn.velocity2, cutIn.smoothTime);
                        cutIn.Transform.position = Vector3.SmoothDamp(cutIn.Transform.position, cutIn.targetPosition, ref cutIn.velocity, cutIn.smoothTime);


                        if ((cutIn.Transform.position - cutIn.targetPosition).sqrMagnitude < 1f && NextMode(cutIn))
                        {
                            b = false;
                            yield break;
                        }
                    }




                    yield return nullobj;
                }
            }



            internal static void firstMode(cutIn cutIn)
            {


                switch (cutIn.mode)
                {
                    case CutInMode.Right2Left:
                        cutIn.Transform.position = new Vector3((float)Screen.width + cutIn.Width / 2f + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.Left2Right:
                        cutIn.Transform.position = new Vector3(-1f * ((float)Screen.width + cutIn.Width / 2f) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.Top2Down:
                        cutIn.Transform.position = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)Screen.height + cutIn.Height / 2f + cutIn.hoseiY);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.Down2Top:
                        cutIn.Transform.position = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, -1f * ((float)Screen.height + cutIn.Height / 2f) + cutIn.hoseiY);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.Back2Front:
                        cutIn.Transform.position = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, Screen.width / 2);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, 0f);
                        cutIn.Transform.localScale = new Vector3(0.1f, 0.1f);
                        cutIn.targetScale = Vector3.one;
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.Front2Back:
                        cutIn.Transform.position = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, Screen.width / 2);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, 0f);
                        cutIn.Transform.localScale = new Vector3(5f, 5f);
                        cutIn.targetScale = Vector3.one;
                        cutIn.alpha = -2f;
                        cutIn.targetAlpha = 1f;
                        break;
                    case CutInMode.None:
                        break;
                }


            }

            internal static bool NextMode(cutIn cutIn)
            {

                // 9.ToLog();

                //Hooks.SaveLog($"{LewdCrest.CutInFertilize_Player != null} {LewdCrest.CutInFertilize_Sound != null}");
                if (PregnancyAssetController.audioObject == null)
                {
                    // 10.ToLog();
                    var audioObject = new GameObject("AudioObject");
                    UnityEngine.Object.DontDestroyOnLoad(audioObject);
                    PregnancyAssetController.audioObjectPtr = audioObject.ToPtr();

                    PregnancyAssetController.CutInFertilize_PlayerPtr = PregnancyAssetController.audioObject.AddComponent(Il2CppType.Of<AudioSource>()).Cast<AudioSource>().ToPtr();
                }
                if (PregnancyAssetController.CutInFertilize_Player == null)
                {
                    // 11.ToLog();
                    PregnancyAssetController.CutInFertilize_PlayerPtr = PregnancyAssetController.audioObject.AddComponent(Il2CppType.Of<AudioSource>()).Cast<AudioSource>().ToPtr();
                }


                if (PregnancyAssetController.CutInFertilize_Player != null && PregnancyAssetController.CutInFertilize_Sound != null)
                {
                    // 12.ToLog();
                    //Hooks.SaveLog($"{LewdCrest.CutInFertilize_Sound.samples}");
                    PregnancyAssetController.CutInFertilize_Player.PlayOneShot(PregnancyAssetController.CutInFertilize_Sound);
                }

                //  13.ToLog();


                switch (cutIn.mode)
                {
                    case CutInMode.None:
                        if (null != cutIn.Transform.parent.gameObject)
                        {

                            UnityEngine.Object.Destroy(cutIn.Transform.parent.gameObject);
                            cutIn.videoPtr = IntPtr.Zero;

                        }

                        return true;
                    case CutInMode.Right2Left:

                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetPosition = new Vector3(0f - cutIn.Width / 2f + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.mode = CutInMode.None;
                        break;
                    case CutInMode.Left2Right:
                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetPosition = new Vector3((float)Screen.width + cutIn.Width / 2f + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY);
                        cutIn.mode = CutInMode.None;
                        break;
                    case CutInMode.Top2Down:
                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, 0f - cutIn.Width / 2f + cutIn.hoseiY);
                        cutIn.mode = CutInMode.None;
                        break;
                    case CutInMode.Down2Top:
                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)Screen.height + cutIn.Height / 2f + cutIn.hoseiY);
                        cutIn.mode = CutInMode.None;
                        break;
                    case CutInMode.Back2Front:
                        //  14.ToLog();
                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetScale = new Vector3(5f, 5f);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, -Screen.width / 2);
                        cutIn.mode = CutInMode.None;
                        break;
                    case CutInMode.Front2Back:
                        cutIn.waitTimer = cutIn.secondTimer;
                        cutIn.alpha = 1f;
                        cutIn.targetAlpha = -2f;
                        cutIn.targetScale = new Vector3(0.1f, 0.1f);
                        cutIn.targetPosition = new Vector3((float)(Screen.width / 2) + cutIn.hoseiX, (float)(Screen.height / 2) + cutIn.hoseiY, -Screen.width / 2);
                        cutIn.mode = CutInMode.None;
                        break;
                }


                return false;
            }
        }
    }


}

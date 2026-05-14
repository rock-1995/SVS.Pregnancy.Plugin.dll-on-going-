using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ActionGame.Chara;
using ADV;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI.Utilities;
using Manager;
#if KKS
using SaveData;
#elif KK
using static SaveData;
#endif
using UnityEngine;

namespace KK_LewdCrest
{
    // Token: 0x0200001C RID: 28
    public static class IMGUIUtils
    {
        #region Custom skins

        internal static bool ColorFilterAffectsImgui =>
#if KK || EC
            true;
#else
            false;
#endif

        /// <summary>
        /// A custom GUISkin with a solid background, sharper edges and less padding.
        /// The skin background color is adjusted to the game (if its color filter affects imgui layer).
        /// Warning: Only use inside OnGUI or things might break.
        /// </summary>
        public static GUISkin SolidBackgroundGuiSkin => InterfaceMaker.CustomSkin;

        private static Texture2D SolidBoxTex { get; set; }

        /// <summary>
        /// Draw a gray non-transparent GUI.Box at the specified rect. Use before a GUI.Window or other controls to get rid of 
        /// the default transparency and make the GUI easier to read.
        /// <example>
        /// IMGUIUtils.DrawSolidBox(screenRect);
        /// GUILayout.Window(362, screenRect, TreeWindow, "Select character folder");
        /// </example>
        /// </summary>
        public static void DrawSolidBox(Rect boxRect)
        {
            if (SolidBoxTex == null)
            {
                var windowBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                windowBackground.SetPixel(0, 0, ColorFilterAffectsImgui ? new Color(0.84f, 0.84f, 0.84f) : new Color(0.4f, 0.4f, 0.4f));
                windowBackground.Apply();
                SolidBoxTex = windowBackground;
                GameObject.DontDestroyOnLoad(windowBackground);
            }

            // It's necessary to make a new GUIStyle here or the texture doesn't show up
            GUI.Box(boxRect, GUIContent.none, new GUIStyle { normal = new GUIStyleState { background = SolidBoxTex } });
        }

        #endregion

        /// <summary>
        /// Block input from going through to the game/canvases if the mouse cursor is within the specified Rect.
        /// Use after a GUI.Window call or the window will not be able to get the inputs either.
        /// <example>
        /// GUILayout.Window(362, screenRect, TreeWindow, "Select character folder");
        /// Utils.EatInputInRect(screenRect);
        /// </example>
        /// </summary>
        /// <param name="eatRect"></param>
        public static void EatInputInRect(Rect eatRect)
        {
            if (eatRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                Input.ResetInputAxes();
        }

        #region Outline controls

        /// <summary>
        /// Draw a label with an outline
        /// </summary>
        /// <param name="rect">Size of the control</param>
        /// <param name="text">Text of the label</param>
        /// <param name="style">Style to be applied to the label</param>
        /// <param name="txtColor">Color of the text</param>
        /// <param name="outlineColor">Color of the outline</param>
        /// <param name="outlineThickness">Thickness of the outline in pixels</param>
        public static void DrawLabelWithOutline(Rect rect, string text, GUIStyle style, Color txtColor, Color outlineColor, int outlineThickness)
        {
            var backupColor = style.normal.textColor;
            var backupGuiColor = GUI.color;

            style.normal.textColor = outlineColor;
            GUI.color = outlineColor;

            var baseRect = rect;

            rect.x -= outlineThickness;
            rect.y -= outlineThickness;

            while (rect.x++ < baseRect.x + outlineThickness)
                GUI.Label(rect, text, style);
            rect.x--;

            while (rect.y++ < baseRect.y + outlineThickness)
                GUI.Label(rect, text, style);
            rect.y--;

            while (rect.x-- > baseRect.x - outlineThickness)
                GUI.Label(rect, text, style);
            rect.x++;

            while (rect.y-- > baseRect.y - outlineThickness)
                GUI.Label(rect, text, style);

            style.normal.textColor = txtColor;
            GUI.color = txtColor;

            GUI.Label(baseRect, text, style);

            style.normal.textColor = backupColor;
            GUI.color = backupGuiColor;
        }

        /// <summary>
        /// Draw a label with a shadow
        /// </summary>        
        /// <param name="rect">Size of the control</param>
        /// <param name="content">Contents of the label</param>
        /// <param name="style">Style to be applied to the label</param>
        /// <param name="txtColor">Color of the outline</param>
        /// <param name="shadowColor">Color of the text</param>
        /// <param name="shadowOffset">Offset of the shadow in pixels</param>
        public static void DrawLabelWithShadow(Rect rect, GUIContent content, GUIStyle style, Color txtColor, Color shadowColor, Vector2 shadowOffset)
        {
            var backupColor = style.normal.textColor;

            style.normal.textColor = shadowColor;
            rect.x += shadowOffset.x;
            rect.y += shadowOffset.y;
            GUI.Label(rect, content, style);

            style.normal.textColor = txtColor;
            rect.x -= shadowOffset.x;
            rect.y -= shadowOffset.y;
            GUI.Label(rect, content, style);

            style.normal.textColor = backupColor;
        }

        /// <inheritdoc cref="DrawLabelWithShadow"/>
        public static void DrawLayoutLabelWithShadow(GUIContent content, GUIStyle style, Color txtColor, Color shadowColor, Vector2 direction, params GUILayoutOption[] options)
        {
            DrawLabelWithShadow(GUILayoutUtility.GetRect(content, style, options), content, style, txtColor, shadowColor, direction);
        }

        /// <inheritdoc cref="DrawLabelWithShadow"/>
        public static bool DrawButtonWithShadow(Rect r, GUIContent content, GUIStyle style, float shadowAlpha, Vector2 direction)
        {
            GUIStyle letters = new GUIStyle(style);
            letters.normal.background = null;
            letters.hover.background = null;
            letters.active.background = null;

            bool result = GUI.Button(r, content, style);

            Color color = r.Contains(Event.current.mousePosition) ? letters.hover.textColor : letters.normal.textColor;

            DrawLabelWithShadow(r, content, letters, color, new Color(0f, 0f, 0f, shadowAlpha), direction);

            return result;
        }

        /// <inheritdoc cref="DrawLabelWithShadow"/>
        public static bool DrawLayoutButtonWithShadow(GUIContent content, GUIStyle style, float shadowAlpha, Vector2 direction, params GUILayoutOption[] options)
        {
            return DrawButtonWithShadow(GUILayoutUtility.GetRect(content, style, options), content, style, shadowAlpha, direction);
        }

        #endregion

        #region Drag / resize window

        private static bool _resizeHandleClicked;
        private static Vector3 _resizeClickedPosition;
        private static Rect _resizeOriginalWindow;
        private static int _resizeCurrentWindowId;

        /// <summary>
        /// Handle both dragging and resizing of OnGUI windows.
        /// Use this instead of GUI.DragWindow(), don't use both at the same time.
        /// To use, place this at the end of your Window method: _windowRect = IMGUIUtils.DragResizeWindow(windowId, _windowRect);
        /// </summary>
        /// <param name="windowId">The ID passed to your window method</param>
        /// <param name="windowRect">The rect of your window. Make sure to set it to the result of this method</param>
        public static Rect DragResizeWindow(int windowId, Rect windowRect)
        {
            const int visibleAreaSize = 13;
            const int functionalAreaSize = 25;

            // Draw a visual hint that resizing is possible
            GUI.Box(new Rect(windowRect.width - visibleAreaSize, windowRect.height - visibleAreaSize, visibleAreaSize, visibleAreaSize), GUIContent.none);

            if (_resizeCurrentWindowId != 0 && _resizeCurrentWindowId != windowId) return windowRect;

            var mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y; // Convert to GUI coords

            var winRect = windowRect;
            var windowHandle = new Rect(
                winRect.x + winRect.width - functionalAreaSize,
                winRect.y + winRect.height - functionalAreaSize,
                functionalAreaSize,
                functionalAreaSize);

            // Can't use Input class because inputs inside of window rect might be eaten
            var mouseButtonDown = Event.current.type == EventType.MouseDown && Event.current.button == 0;
            if (mouseButtonDown && windowHandle.Contains(mousePos))
            {
                _resizeHandleClicked = true;
                _resizeClickedPosition = mousePos;
                _resizeOriginalWindow = winRect;
                _resizeCurrentWindowId = windowId;
            }

            if (_resizeHandleClicked)
            {
                // Resize window by dragging
                var listWinRect = winRect;
                listWinRect.width = Mathf.Clamp(_resizeOriginalWindow.width + (mousePos.x - _resizeClickedPosition.x), 100, Screen.width);
                listWinRect.height = Mathf.Clamp(_resizeOriginalWindow.height + (mousePos.y - _resizeClickedPosition.y), 100, Screen.height);
                windowRect = listWinRect;

                var mouseButtonUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
                if (mouseButtonUp)
                {
                    _resizeHandleClicked = false;
                    _resizeCurrentWindowId = 0;
                }
            }
            else
            {
                // Handle dragging only if not resizing else things break
                GUI.DragWindow();
            }
            return windowRect;
        }

        /// <summary>
        /// Handle both dragging and resizing of OnGUI windows, as well as eat mouse inputs when cursor is over the window.
        /// Use this instead of <see cref="GUI.DragWindow(Rect)"/> and <see cref="EatInputInRect"/>. Don't use these methods at the same time as DragResizeEatWindow.
        /// To use, place this at the end of your Window method: _windowRect = IMGUIUtils.DragResizeEatWindow(windowId, _windowRect);
        /// </summary>
        /// <param name="windowId">The ID passed to your window method</param>
        /// <param name="windowRect">The rect of your window. Make sure to set it to the result of this method</param>
        public static Rect DragResizeEatWindow(int windowId, Rect windowRect)
        {
            var result = DragResizeWindow(windowId, windowRect);
            EatInputInRect(result);
            return result;
        }

        #endregion

        private static GUIStyle _tooltipStyle;
        private static GUIContent _tooltipContent;
        private static Texture2D _tooltipBackground;
        /// <summary>
        /// Display a tooltip for any GUIContent with the tootlip property set in a given window.
        /// To use, place this at the end of your Window method: IMGUIUtils.DrawTooltip(_windowRect);
        /// </summary>
        /// <param name="area">Area where the tooltip can appear</param>
        /// <param name="tooltipWidth">Minimum width of the tooltip, can't be larger than area's width</param>
        public static void DrawTooltip(Rect area, int tooltipWidth = 400)
        {
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                if (_tooltipBackground == null)
                {
                    _tooltipBackground = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                    _tooltipBackground.SetPixel(0, 0, Color.black);
                    _tooltipBackground.Apply();

                    _tooltipStyle = new GUIStyle
                    {
                        normal = new GUIStyleState { textColor = Color.white, background = _tooltipBackground },
                        wordWrap = true,
                        alignment = TextAnchor.MiddleCenter
                    };
                    _tooltipContent = new GUIContent();
                }

                var lines = GUI.tooltip.Split('\n');
                var longestLine = lines.OrderByDescending(l => l.Length).First();

                _tooltipContent.text = longestLine;
                _tooltipStyle.CalcMinMaxWidth(_tooltipContent, out var minWidth, out var maxWidth);

                var areaWidth = (int)area.width;
                if (maxWidth > areaWidth) maxWidth = areaWidth;
                if (tooltipWidth > areaWidth) tooltipWidth = areaWidth;

                _tooltipContent.text = GUI.tooltip;
                var height = _tooltipStyle.CalcHeight(_tooltipContent, tooltipWidth) + 10;

                var heightP = height / area.height;
                //var widthP = maxWidth / areaWidth;
                var squareWidth = areaWidth * (heightP + 0.05f);
                if (squareWidth > tooltipWidth)
                {
                    tooltipWidth = Mathf.Min((int)maxWidth, (int)squareWidth);
                    height = _tooltipStyle.CalcHeight(_tooltipContent, tooltipWidth) + 10;
                }

                var currentEvent = Event.current;

                var x = currentEvent.mousePosition.x + tooltipWidth > area.width
                    ? area.width - tooltipWidth
                    : currentEvent.mousePosition.x;

                var y = currentEvent.mousePosition.y + 25 + height > area.height
                    ? currentEvent.mousePosition.y - height - 15
                    : currentEvent.mousePosition.y + 25;

                GUI.Box(new Rect(x, y, tooltipWidth, height), GUI.tooltip, _tooltipStyle);
            }
        }
    }

    internal static class Utils
    {
        // Token: 0x0600012F RID: 303 RVA: 0x00008A7E File Offset: 0x00006C7E
        public static SaveData.Heroine GetLeadHeroine(this HFlag hflag)
        {
            return hflag.lstHeroine[hflag.GetLeadHeroineId()];
        }

        // Token: 0x06000130 RID: 304 RVA: 0x00008A91 File Offset: 0x00006C91
        public static int GetLeadHeroineId(this HFlag hflag)
        {
            if (hflag.mode != HFlag.EMode.houshi3P && hflag.mode != HFlag.EMode.sonyu3P)
            {
                return 0;
            }
            return hflag.nowAnimationInfo.id % 2;
        }

        // Token: 0x06000131 RID: 305 RVA: 0x00008AB4 File Offset: 0x00006CB4
        public static SaveData.Heroine GetCurrentVisibleGirl()
        {
#if KK
            if (!Singleton<Game>.IsInstance())
            {
                return null;
            }
            
#elif KKS
            if (!Singleton<Game>.IsInstance())
            {
                return null;
            }
#endif
#if KK
            if (Singleton<Game>.Instance.actScene != null && Singleton<Game>.Instance.actScene.AdvScene != null)
            {
                ADVScene advScene = Singleton<Game>.Instance.actScene.AdvScene;
#elif KKS
            if (SingletonInitializer<ActionScene>.instance != null && SingletonInitializer<ActionScene>.instance.AdvScene != null)
            {
                ADVScene advScene = SingletonInitializer<ActionScene>.instance.AdvScene;
#else 
            if(true)
            {
#endif

            TextScenario scenario = advScene.Scenario;
                if (((scenario != null) ? scenario.currentHeroine : null) != null)
                {
                    
                    return advScene.Scenario.currentHeroine;
                }
                TalkScene talkScene;
                
                if ((talkScene = (advScene.nowScene as TalkScene)) != null)
                {
#if KK
                    return (SaveData.Heroine)AccessTools.Field(typeof(TalkScene), "targetHeroine").GetValue(talkScene);
#elif KKS
                    return (SaveData.Heroine)AccessTools.Field(typeof(TalkScene), "<targetHeroine>k__BackingField").GetValue(talkScene);
                    
#endif

                }
            }
            
            TalkScene talkScene2 = UnityEngine.Object.FindObjectOfType<TalkScene>();
            if (talkScene2 == null)
            {
                
                return null;
            }
#if KK
            SaveData.Heroine heroine= (SaveData.Heroine)AccessTools.Field(typeof(TalkScene), "targetHeroine").GetValue(talkScene2);
#elif KKS
            SaveData.Heroine heroine = (SaveData.Heroine)AccessTools.Field(typeof(TalkScene), "<targetHeroine>k__BackingField").GetValue(talkScene2);
#endif

            return heroine;
        }

        // Token: 0x06000132 RID: 306 RVA: 0x00008B54 File Offset: 0x00006D54
        private static ChaControl GetCurrentVisibleGirlChaControl()
        {
            TalkScene talkScene = UnityEngine.Object.FindObjectOfType<TalkScene>();
            SaveData.Heroine heroine = (talkScene != null) ? (SaveData.Heroine)AccessTools.Field(typeof(TalkScene), "targetHeroine").GetValue(talkScene) : null;
            if (heroine != null)
            {
                return heroine.chaCtrl;
            }
            Game instance = Singleton<Game>.Instance;
            ADVScene advscene;
            if (instance == null)
            {
                advscene = null;
            }
            else
            {
#if KK
                ActionScene actScene = instance.actScene;
#elif KKS
                ActionScene actScene = SingletonInitializer<ActionScene>.instance;
#endif
                advscene = ((actScene != null) ? actScene.AdvScene : null);
            }
            ADVScene advscene2 = advscene;
            if (advscene2 == null)
            {
                return null;
            }
            TextScenario scenario = advscene2.Scenario;
            if (((scenario != null) ? scenario.currentHeroine : null) != null)
            {
                return advscene2.Scenario.currentHeroine.chaCtrl;
            }
            Character instance2 = Singleton<Character>.Instance;
#if KK
            if (instance2 != null && instance2.dictEntryChara.Count > 0)
            {
                return instance2.dictEntryChara[0];
            }
#elif KKS
            if (instance2 != null && Character.dictEntryChara.Count > 0)
            {
                return Character.dictEntryChara[0];
            }
#endif
            ChaControl result;
            try
            {
                FieldInfo field = typeof(ADVScene).GetField("m_TargetHeroine", BindingFlags.Instance | BindingFlags.NonPublic);
                SaveData.Heroine heroine2 = ((field != null) ? field.GetValue(advscene2.nowScene) : null) as SaveData.Heroine;
                result = ((heroine2 != null) ? heroine2.chaCtrl : null);
            }
            catch
            {
                result = null;
            }
            return result;
        }

        // Token: 0x06000133 RID: 307 RVA: 0x00008C84 File Offset: 0x00006E84
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> self)
        {
            return from _ in self
                   orderby Guid.NewGuid()
                   select _;
        }

        // Token: 0x06000134 RID: 308 RVA: 0x00008CAB File Offset: 0x00006EAB
        public static int RandomValue(params int[] _array)
        {
            return _array.Shuffle<int>().First<int>();
        }

        // Token: 0x06000135 RID: 309 RVA: 0x00008CB8 File Offset: 0x00006EB8
        public static Dictionary<int, string> ActionDict()
        {
            Dictionary<int, string> dictionary = new Dictionary<int, string>();
            dictionary[-1] = "ウロウロ";
            dictionary[0] = "着替える";
            dictionary[1] = "トイレで用を足す";
            dictionary[2] = "シャワーを浴びる";
            dictionary[3] = "食事をする";
            dictionary[4] = "Ｈしたい(自慰)";
            dictionary[5] = "Ｈしたい(主人公)";
            dictionary[6] = "部活したい";
            dictionary[7] = "女の子と会話したい";
            dictionary[8] = "主人公と会話したい";
            dictionary[9] = "読書したい";
            dictionary[10] = "スマホをいじる";
            dictionary[11] = "スマホでゲーム";
            dictionary[12] = "スマホで自撮り";
            dictionary[13] = "音楽を聴く";
            dictionary[14] = "ダンス練習";
            dictionary[15] = "容姿を整えたい";
            dictionary[16] = "飲み物を飲みたい";
            dictionary[17] = "気分転換したい";
            dictionary[18] = "運動したい";
            dictionary[19] = "性格個別行動";
            dictionary[20] = "逃走";
            dictionary[21] = "勉強する";
            dictionary[22] = "寝る";
            dictionary[23] = "ついてきて用";
            dictionary[24] = "おーい用";
            dictionary[25] = "恥ずかしがり用";
            dictionary[26] = "レズ(呼び出し側)";
            dictionary[27] = "レズ相手(実際にレズ)";
            dictionary[28] = "会話したい返答待ち";
            dictionary[29] = "Ｈしたい返答待ち";
            dictionary[30] = "モジモジ待機";
            dictionary[400] = "ハードルオナニー";
            return dictionary;
        }

        // Token: 0x06000136 RID: 310 RVA: 0x00008E70 File Offset: 0x00007070
        public static string ActionNoInterpreter(int no)
        {
            string result = string.Empty;
            if (!Utils.ActionDict().TryGetValue(no, out result))
            {
                result = "unknown " + no.ToString();
            }
            return result;
        }

        // Token: 0x06000137 RID: 311 RVA: 0x00008EA5 File Offset: 0x000070A5
        public static LewdCrestGameController GetGameController()
        {
            return UnityEngine.Object.FindObjectOfType<LewdCrestGameController>();
        }

        // Token: 0x06000138 RID: 312 RVA: 0x00008EAC File Offset: 0x000070AC
        public static LewdCrestController GetController(SaveData.Player player)
        {
            if (!(((player != null) ? player.chaCtrl : null) != null))
            {
                return null;
            }
            return player.chaCtrl.GetComponent<LewdCrestController>();
        }

        // Token: 0x06000139 RID: 313 RVA: 0x00008EAC File Offset: 0x000070AC
        public static LewdCrestController GetController(SaveData.Heroine heroine)
        {
            if (!(((heroine != null) ? heroine.chaCtrl : null) != null))
            {
                return null;
            }
            return heroine.chaCtrl.GetComponent<LewdCrestController>();
        }

        // Token: 0x0600013A RID: 314 RVA: 0x00008ECF File Offset: 0x000070CF
        public static NPC GetNPC(this AI ai)
        {
            return Traverse.Create(ai).Property("npc", null).GetValue<NPC>();
        }

        // Token: 0x0600013B RID: 315 RVA: 0x00008EE8 File Offset: 0x000070E8
        public static Queue<int> GetLastActions(this AI ai)
        {
            IDictionary value = Traverse.Create(Traverse.Create(ai).Property("actScene", null).GetValue<ActionScene>().actCtrl).Field("dicTarget").GetValue<IDictionary>();
            NPC npc = ai.GetNPC();
            return Traverse.Create(value[npc.heroine]).Field("_queueAction").GetValue<Queue<int>>();
        }

        // Token: 0x0600013C RID: 316 RVA: 0x00008F4A File Offset: 0x0000714A
        public static bool IsExitingScene(this NPC npc)
        {
            return npc.isActive;
        }

        // Token: 0x0600013D RID: 317 RVA: 0x00008F52 File Offset: 0x00007152
        public static bool IsHExperience(SaveData.Heroine girl)
        {
            return girl.HExperience == SaveData.Heroine.HExperienceKind.慣れ || girl.HExperience == SaveData.Heroine.HExperienceKind.淫乱;
        }

        // Token: 0x0600013E RID: 318 RVA: 0x00008F68 File Offset: 0x00007168
        public static bool IsPregnancyGirl(SaveData.Heroine girl)
        {
            return girl != null && Utils.IsPregnancyGirl(girl.charFile);
        }

        // Token: 0x0600013F RID: 319 RVA: 0x00008F7A File Offset: 0x0000717A
        public static bool IsPregnancyGirl(ChaFileControl charFile)
        {
            return Utils.GetPregnancyWeek(charFile) > 0;
        }

        // Token: 0x06000140 RID: 320 RVA: 0x00008F85 File Offset: 0x00007185
        public static float GetBellySizePercent(ChaFileControl charFile)
        {
            return Mathf.Clamp01(((float)Utils.GetPregnancyWeek(charFile) - 1f) / ((float)Utils.LeaveSchoolWeek - 1f));
        }

        // Token: 0x06000141 RID: 321 RVA: 0x00008FA8 File Offset: 0x000071A8
        public static void PregnancyDeserializeData(PluginData data, out int week, out bool gameplayEnabled, out float fertility, out int pregnancyCount, out int weeksSinceLastPregnancy, out Utils.MenstruationSchedule schedule)
        {
            week = 0;
            gameplayEnabled = true;
            fertility = Utils.DefaultFertility;
            pregnancyCount = 0;
            weeksSinceLastPregnancy = 0;
            schedule = Utils.MenstruationSchedule.Default;
            if (((data != null) ? data.data : null) == null)
            {
                return;
            }
            object obj;
            object obj2;
            if (data.data.TryGetValue("Week", out obj) && (obj2 = obj) is int)
            {
                int num = (int)obj2;
                week = num;
            }
            object obj3;
            if (data.data.TryGetValue("GameplayEnabled", out obj3) && (obj2 = obj3) is bool)
            {
                bool flag = (bool)obj2;
                gameplayEnabled = flag;
            }
            object obj4;
            if (data.data.TryGetValue("Fertility", out obj4) && (obj2 = obj4) is float)
            {
                float num2 = (float)obj2;
                fertility = num2;
            }
            object obj5;
            if (data.data.TryGetValue("PregnancyCount", out obj5) && (obj2 = obj5) is int)
            {
                int num3 = (int)obj2;
                pregnancyCount = num3;
            }
            object obj6;
            if (data.data.TryGetValue("WeeksSinceLastPregnancy", out obj6) && (obj2 = obj6) is int)
            {
                int num4 = (int)obj2;
                weeksSinceLastPregnancy = num4;
            }
            object obj7;
            if (data.data.TryGetValue("MenstruationSchedule", out obj7) && (obj2 = obj7) is int)
            {
                int num5 = (int)obj2;
                schedule = (Utils.MenstruationSchedule)num5;
            }
            if (week > 0)
            {
                weeksSinceLastPregnancy = 0;
                if (pregnancyCount == 0)
                {
                    pregnancyCount = 1;
                }
            }
        }

        // Token: 0x06000142 RID: 322 RVA: 0x000090FC File Offset: 0x000072FC
        public static PluginData PregnancySerializeData(int week, bool gameplayEnabled, float fertility, int pregnancyCount, int weeksSinceLastPregnancy, Utils.MenstruationSchedule schedule)
        {
            if (week <= 0 && gameplayEnabled && Mathf.Approximately(fertility, Utils.DefaultFertility))
            {
                return null;
            }
            PluginData pluginData = new PluginData();
            pluginData.version = 1;
            pluginData.data["Week"] = week;
            pluginData.data["GameplayEnabled"] = gameplayEnabled;
            pluginData.data["Fertility"] = fertility;
            pluginData.data["PregnancyCount"] = pregnancyCount;
            pluginData.data["WeeksSinceLastPregnancy"] = weeksSinceLastPregnancy;
            pluginData.data["MenstruationSchedule"] = (int)schedule;
            return pluginData;
        }

        // Token: 0x06000143 RID: 323 RVA: 0x000091B5 File Offset: 0x000073B5
        public static float GetFertility(SaveData.Heroine girl)
        {
            if (girl == null)
            {
                return 0f;
            }
            return Utils.GetFertility(girl.charFile);
        }

        // Token: 0x06000144 RID: 324 RVA: 0x000091CC File Offset: 0x000073CC
        public static float GetFertility(ChaFileControl charFile)
        {
            float result;
            try
            {
                int num;
                bool flag;
                float num2;
                int num3;
                int num4;
                Utils.MenstruationSchedule menstruationSchedule;
                Utils.PregnancyDeserializeData(ExtendedSave.GetExtendedDataById(charFile, Utils.PregnancyPlugin_GUID), out num, out flag, out num2, out num3, out num4, out menstruationSchedule);
                result = num2;
            }
            catch (Exception ex)
            {
                LewdCrest.Log("GetFertility ERR=" + ex.ToString(), true);
                result = 0f;
            }
            return result;
        }

        // Token: 0x06000145 RID: 325 RVA: 0x00009230 File Offset: 0x00007430
        public static int GetPregnancyWeek(SaveData.Heroine girl)
        {
            if (girl == null)
            {
                return 0;
            }
            return Utils.GetPregnancyWeek(girl.charFile);
        }

        // Token: 0x06000146 RID: 326 RVA: 0x00009244 File Offset: 0x00007444
        public static int GetPregnancyWeek(ChaFileControl charFile)
        {
            int result;
            try
            {
                int num;
                bool flag;
                float num2;
                int num3;
                int num4;
                Utils.MenstruationSchedule menstruationSchedule;
                Utils.PregnancyDeserializeData(ExtendedSave.GetExtendedDataById(charFile, Utils.PregnancyPlugin_GUID), out num, out flag, out num2, out num3, out num4, out menstruationSchedule);
                if (!flag)
                {
                    result = 0;
                }
                else
                {
                    LewdCrest.Log(string.Format("GetPregnancyWeek={0}", num), false);
                    result = num;
                }
            }
            catch (Exception ex)
            {
                LewdCrest.Log("GetPregnancyWeek ERR=" + ex.ToString(), true);
                result = 0;
            }
            return result;
        }

        // Token: 0x06000147 RID: 327 RVA: 0x000092C0 File Offset: 0x000074C0
        public static void SetPregnancyWeek(SaveData.Heroine girl, int newWeek)
        {
            if (girl == null)
            {
                return;
            }
            Utils.SetPregnancyWeek(girl.charFile, newWeek);
        }

        // Token: 0x06000148 RID: 328 RVA: 0x000092D4 File Offset: 0x000074D4
        public static void SetPregnancyWeek(ChaFileControl charFile, int newWeek)
        {
            try
            {
                int num;
                bool gameplayEnabled;
                float fertility;
                int num2;
                int num3;
                Utils.MenstruationSchedule schedule;
                Utils.PregnancyDeserializeData(ExtendedSave.GetExtendedDataById(charFile, Utils.PregnancyPlugin_GUID), out num, out gameplayEnabled, out fertility, out num2, out num3, out schedule);
                num2++;
                LewdCrest.Log(string.Format("SetPregnancyWeek={0}", newWeek), false);
                LewdCrest.Log(string.Format(" pregnancyCount={0}", num2), false);
                LewdCrest.Log(string.Format(" weeksSinceLastPregnancy={0}", num3), false);
                ExtendedSave.SetExtendedDataById(charFile, Utils.PregnancyPlugin_GUID, Utils.PregnancySerializeData(newWeek, gameplayEnabled, fertility, num2, 0, schedule));
            }
            catch (Exception ex)
            {
                LewdCrest.Log("SetPregnancyWeek ERR=" + ex.ToString(), true);
            }
        }

        // Token: 0x040000D5 RID: 213
        private static readonly string PregnancyPlugin_GUID = "KK_Pregnancy";

        // Token: 0x040000D6 RID: 214
        private static readonly float DefaultFertility = 0.3f;

        // Token: 0x040000D7 RID: 215
        private static readonly int LeaveSchoolWeek = 41;

        // Token: 0x0200001D RID: 29
        public enum MenstruationSchedule
        {
            // Token: 0x040000D9 RID: 217
            Default,
            // Token: 0x040000DA RID: 218
            MostlyRisky,
            // Token: 0x040000DB RID: 219
            AlwaysSafe,
            // Token: 0x040000DC RID: 220
            AlwaysRisky
        }
    }
}

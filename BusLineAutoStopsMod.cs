using System;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using Unity.Entities;
using UnityEngine.InputSystem;
using BusLineAutoStops.Patches;
using BusLineAutoStops.Systems;

namespace BusLineAutoStops
{
    /// <summary>
    /// BusLine AutoStops 入口。
    /// LoadSettings → 多语言 → RegisterInOptionsUI → RegisterKeyBindings → Harmony → 注册系统。
    /// </summary>
    public class BusLineAutoStopsMod : IMod
    {
        public const string kVersion = "0.1.1";

        public static ILog log = LogManager.GetLogger("BusLineAutoStops").SetShowsErrorsInUI(false);

        public static BusLineAutoStopsMod Instance;

        private Setting m_Setting;
        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            log.Info("BusLine AutoStops v" + kVersion + " loading (local build, not published)...");

            m_Setting = new Setting(this);
            Setting.Instance = m_Setting;

            // 多语言：每份字典各自构建；设置页 + 游戏内面板。
            try
            {
                string[] locales = LocaleTable.kLocales;
                string active = "en-US";
                try
                {
                    string[] supported = GameManager.instance.localizationManager.GetSupportedLocales();
                    if (supported != null && supported.Length > 0) locales = supported;
                    active = GameManager.instance.localizationManager.activeLocaleId;
                }
                catch (Exception) { }
                LocaleTable.SetActiveLocale(active);

                for (int i = 0; i < locales.Length; i++)
                {
                    GameManager.instance.localizationManager.AddSource(locales[i], new SettingLocaleSource(m_Setting, locales[i]));
                    GameManager.instance.localizationManager.AddSource(locales[i], new PanelLocaleSource(locales[i]));
                }
                log.Info("语言源已注册 " + locales.Length + " 份，当前=" + active + " setting.id=" + m_Setting.id);
            }
            catch (Exception e)
            {
                log.Warn("注册语言源失败：" + e.GetType().Name + " " + e.Message);
            }

            if (World.DefaultGameObjectInjectionWorld != null)
            {
                m_Setting.RegisterInOptionsUI();
                log.Info("选项页已注册");
            }
            else
            {
                log.Warn("世界未就绪，选项页未注册");
            }

            try
            {
                Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(
                    nameof(BusLineAutoStops), m_Setting, new Setting(this));
            }
            catch (Exception e)
            {
                log.Warn("读取设置失败，用默认值：" + e.GetType().Name);
            }
            m_Setting.SyncToState();

            RegisterHotkeys();

            m_Harmony = new Harmony("com.buslineautostops");
            int patched = 0;
            string hookErr = null;
            try
            {
                patched = RouteToolPatches.Install(m_Harmony, out hookErr);
                patched += ObjectSelectPatches.Install(m_Harmony, out hookErr);
            }
            catch (Exception e)
            {
                hookErr = e.GetType().Name + ": " + e.Message;
            }
            log.Info("[Hook] 补丁 " + patched + (hookErr != null ? "；问题：" + hookErr : " 个已安装"));

            try
            {
                updateSystem.UpdateAt<AutoPlaceSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<StopGhostSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<UiCommandSystem>(SystemUpdatePhase.UIUpdate);
                log.Info("系统已注册：AutoPlace / StopGhost / UiCommand");
            }
            catch (Exception e)
            {
                LazyModeState.Broken = true;
                log.Error("系统注册失败，本模组不介入：" + e.GetType().Name + " " + e.Message);
            }
        }

        public void OnDispose()
        {
            try
            {
                if (m_Harmony != null)
                {
                    m_Harmony.UnpatchAll(m_Harmony.Id);
                    m_Harmony = null;
                }
            }
            catch (Exception) { }
            Setting.Instance = null;
            Instance = null;
            log.Info("BusLine AutoStops disposed");
        }

        private void RegisterHotkeys()
        {
            try
            {
                m_Setting.RegisterKeyBindings();
                ProxyAction a = m_Setting.GetAction(Setting.kActionToggleLazy);
                if (a == null)
                {
                    log.Warn("[快捷键] 未取得 action");
                    return;
                }

                ProxyBinding b = m_Setting.ToggleLazyBinding;
                if (IsBindablePath(b.path))
                {
                    a.shouldBeEnabled = true;
                }
                else
                {
                    a.shouldBeEnabled = false;
                    log.Info("[快捷键] 未绑定，不启用 action（防幻影触发）");
                }

                a.onInteraction += (action, phase) =>
                {
                    if (phase != InputActionPhase.Started) return;
                    try { OnToggleLazy(); }
                    catch (Exception e) { log.Warn("快捷键处理失败：" + e.Message); }
                };
                log.Info("[快捷键] ToggleLazyMode 已注册 path=" + (b.path ?? "(empty)"));
            }
            catch (Exception e)
            {
                log.Warn("注册快捷键失败：" + e.GetType().Name);
            }
        }

        /// <summary>未绑键路径不得启用 action（Playbook §3.2 幻影触发）。</summary>
        public static bool IsBindablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Trim();
            if (p.Length == 0) return false;
            if (p == "<Keyboard>/" || p == "<Keyboard>") return false;
            // 至少要有设备后的具体绑定
            int slash = p.IndexOf('/');
            if (slash < 0 || slash >= p.Length - 1) return false;
            return true;
        }

        /// <summary>快捷键 / 前端按钮 共用入口。</summary>
        public void OnToggleLazy()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            // 先刷新一次「当前选中站台」，避免依赖 SetPrefab 钩子
            StationSelectTracker.EnsureFresh();

            if (LazyModeState.LazyEnabled)
            {
                ExitLazyMode();
                return;
            }

            log.Info("[懒人模式] 尝试进入 selected=" + LazyModeState.SelectedStationPrefabKey
                     + " kind=" + LazyModeState.SelectedStationKind);

            if (LazyModeState.SelectedStationKind == TransportKind.None
                || string.IsNullOrEmpty(LazyModeState.SelectedStationPrefabKey))
            {
                LazyModeState.ShowWarning(LocaleTable.PanelKey("WARN_SELECT_STATION"), 3f, now);
                log.Info("[懒人模式] 拒绝进入：未选择站台资产");
                return;
            }

            if (!TransportKindRules.IsLazyModeSupported(LazyModeState.SelectedStationKind))
            {
                LazyModeState.ShowWarning(LocaleTable.PanelKey("WARN_NOT_BUS"), 3f, now);
                log.Info("[懒人模式] 拒绝进入：类型 " + LazyModeState.SelectedStationKind + " 未启用");
                return;
            }

            LazyModeState.ModeKind = LazyModeState.SelectedStationKind;
            LazyModeState.LazyEnabled = true;
            LazyModeState.UiSeq++;
            log.Info("[懒人模式] 开启 kind=" + LazyModeState.ModeKind + " prefab=" + LazyModeState.SelectedStationPrefabKey);

            // 切换到创建线路工具（公交）。失败不阻断懒人模式状态。
            try
            {
                RouteToolPatches.ActivateBusLineTool();
            }
            catch (Exception e)
            {
                log.Warn("切换线路工具失败：" + e.Message);
            }
        }

        private void ExitLazyMode()
        {
            ExitDeletePolicy policy = LazyModeState.ExitPolicy;
            LazyModeState.LazyEnabled = false;
            LazyModeState.UiSeq++;

            var stops = new System.Collections.Generic.List<string>();
            var stations = new System.Collections.Generic.List<string>();
            PlacedStopRegistry.CollectExitDeletes(policy, stops, stations);

            if (stops.Count > 0 || stations.Count > 0)
            {
                try
                {
                    AutoPlaceSystem.ApplyExitDeletes(stops, stations);
                    log.Info("[懒人模式] 退出清理 stops=" + stops.Count + " stations=" + stations.Count + " policy=" + policy);
                }
                catch (Exception e)
                {
                    log.Warn("退出清理失败：" + e.Message);
                }
            }
            else
            {
                log.Info("[懒人模式] 关闭 policy=" + policy + "（无删除或无记录）");
            }

            PlacedStopRegistry.ClearSession();
            LazyModeState.ModeKind = TransportKind.None;
        }
    }
}

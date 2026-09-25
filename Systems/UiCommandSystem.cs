using Game.UI;
using Colossal.UI.Binding;
using Game;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// 向前端推送：懒人模式开关状态、警告词条 key。
    /// 前端 GameTopLeft 按钮只读这些绑定；文案 id 走 cs2/l10n / Translate（BLG 可翻）。
    /// </summary>
    public partial class UiCommandSystem : UISystemBase
    {
        public const string kGroup = "BusLineAutoStops";

        private GetterValueBinding<string> m_stateBinding;
        private int m_lastSeq = -1;
        private string m_lastJson = "";

        protected override void OnCreate()
        {
            base.OnCreate();
            AddBinding(m_stateBinding = new GetterValueBinding<string>(
                kGroup, "GetUiState", GetUiState));
            AddBinding(new CallBinding<string, string>(kGroup, "CallToggleLazy", _ =>
            {
                CallToggle();
                return "ok";
            }));
        }

        private string GetUiState()
        {
            // 轻量 JSON，避免额外依赖
            int seq = LazyModeState.UiSeq;
            if (seq == m_lastSeq && m_lastJson.Length > 0) return m_lastJson;

            float now = UnityEngine.Time.realtimeSinceStartup;
            bool warn = LazyModeState.HasWarning(now);
            string warnKey = warn ? LazyModeState.WarningLocaleKey : "";
            string json = "{\"enabled\":"
                + (LazyModeState.LazyEnabled ? "true" : "false")
                + ",\"seq\":" + seq
                + ",\"warningKey\":\"" + Escape(warnKey) + "\""
                + ",\"modeKind\":" + (int)LazyModeState.ModeKind
                + "}";
            m_lastSeq = seq;
            m_lastJson = json;
            return json;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>前端点击按钮时调用。</summary>
        private void CallToggle()
        {
            try
            {
                if (BusLineAutoStopsMod.Instance != null) BusLineAutoStopsMod.Instance.OnToggleLazy();
            }
            catch (System.Exception e)
            {
                BusLineAutoStopsMod.log.Warn("UI Toggle 失败：" + e.Message);
            }
        }

        protected override void OnUpdate()
        {
            LazyModeState.ClearWarningIfExpired(UnityEngine.Time.realtimeSinceStartup);
            StationSelectTracker.Poll(World);
        }
    }
}

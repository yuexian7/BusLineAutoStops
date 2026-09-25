using System;

namespace BusLineAutoStops
{
    /// <summary>
    /// BridgeTheLanguageGap 兼容的 key 规则（纯函数，可离线测）。
    ///
    /// BLG 只 Hook UILocalizationManager.Translate，且 Scope.Classify 把
    ///   Options.* / Common.ACTION[…] 且 identifier 以 ModSetting.instances 的 key 为前缀
    /// 的词条归入「模组选项及说明」。identifier 取 key 里第一个 '[' 到最后一个 ']'。
    /// instances 的 key ≈ setting.id = "程序集.命名空间.Mod类"。
    ///
    /// 因此面板文案必须：① 进 LocalizationDictionary（AddSource）② 前端用 cs2/l10n 渲染
    /// ③ key 形如 Options.…[setting.id…] 或 Common.ACTION[setting.id…]。
    /// 禁止前端 getString/硬编码直出 —— 那正是 TrafficToolEssentials 面板翻不动的原因。
    /// </summary>
    public static class BlgKeys
    {
        /// <summary>与 Setting.kSettingId 一致；此处重复一份供纯逻辑测试。</summary>
        public const string SettingId = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";

        /// <summary>BLG Scope.ExtractIdentifier：第一个 '[' 到最后一个 ']'。</summary>
        public static string ExtractIdentifier(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int a = key.IndexOf('[');
            int b = key.LastIndexOf(']');
            if (a < 0 || b <= a) return null;
            return key.Substring(a + 1, b - a - 1);
        }

        /// <summary>BLG Scope.IsModIdentifier 的判定形状：ident.StartsWith(instanceKey)。</summary>
        public static bool IsModIdentifier(string key, string instanceKey)
        {
            if (string.IsNullOrEmpty(instanceKey)) return false;
            string ident = ExtractIdentifier(key);
            if (string.IsNullOrEmpty(ident)) return false;
            return ident.StartsWith(instanceKey, StringComparison.Ordinal);
        }

        /// <summary>是否会被 BLG 归入 ModOptions（Options.* 或 Common.ACTION[…] 且 id 命中）。</summary>
        public static bool WouldClassifyAsModOptions(string key, string instanceKey)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool prefixOk = key.StartsWith("Options.", StringComparison.Ordinal)
                || key.StartsWith("Common.ACTION[", StringComparison.Ordinal);
            return prefixOk && IsModIdentifier(key, instanceKey);
        }

        /// <summary>
        /// 游戏内面板 key。identifier = setting.id + ".Panel." + slug，
        /// 无论 instances 收的是完整 setting.id 还是更短前缀（BusLineAutoStops…）都能 StartsWith 命中。
        /// </summary>
        public static string PanelKey(string slug)
        {
            return "Options.BUSLINEAUTOSTOPS." + slug + "[" + SettingId + ".Panel." + slug + "]";
        }

        /// <summary>同文案的 Common.ACTION 形态（BLG 对该前缀同样归 ModOptions）。</summary>
        public static string PanelActionKey(string slug)
        {
            return "Common.ACTION[" + SettingId + ".Panel." + slug + "]";
        }

        /// <summary>下拉枚举成员 key：Options.&lt;id&gt;.&lt;ENUMNAME&gt;[成员]（与 AutomaticSettings 一致）。</summary>
        public static string EnumKey(string enumTypeName, string member)
        {
            return "Options." + SettingId + "." + enumTypeName + "[" + member + "]";
        }
    }
}

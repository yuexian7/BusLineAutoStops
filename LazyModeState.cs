using System;

namespace BusLineAutoStops
{
    /// <summary>
    /// 热路径状态镜像（Playbook 硬规则 15：hook/每帧不读设置属性链）。
    /// 所有字段 volatile；由 Setting.Sync() / UI 命令写入，Systems/Patches 只读。
    /// </summary>
    public static class LazyModeState
    {
        public static volatile bool LazyEnabled;
        public static volatile TransportKind ModeKind = TransportKind.None;

        /// <summary>当前选中的站台 prefab（Entity.Index 组成的稳定字符串；Null 表示未选）。</summary>
        public static volatile string SelectedStationPrefabKey = "";

        /// <summary>当前选中站台的类型。</summary>
        public static volatile TransportKind SelectedStationKind = TransportKind.None;

        public static volatile ExitDeletePolicy ExitPolicy = ExitDeletePolicy.DoNothing;

        /// <summary>前端按钮高亮 / 警告文案 key（空=无警告）。</summary>
        public static volatile string WarningLocaleKey = "";
        public static volatile float WarningUntilRealtime;

        public static volatile bool Broken;

        /// <summary>UI 推送用序号，前端 bindValue 轮询。</summary>
        public static int UiSeq;

        public static void ShowWarning(string localeKey, float durationSeconds, float nowRealtime)
        {
            WarningLocaleKey = localeKey ?? "";
            WarningUntilRealtime = nowRealtime + durationSeconds;
            UiSeq++;
        }

        public static void ClearWarningIfExpired(float nowRealtime)
        {
            if (WarningLocaleKey.Length > 0 && nowRealtime > WarningUntilRealtime)
            {
                WarningLocaleKey = "";
                UiSeq++;
            }
        }

        public static bool HasWarning(float nowRealtime)
        {
            return WarningLocaleKey.Length > 0 && nowRealtime <= WarningUntilRealtime;
        }
    }
}

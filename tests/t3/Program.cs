using System.Collections.Generic;

namespace BusLineAutoStops.Tests
{
    /// <summary>
    /// 离线 T3：纯逻辑断言（零游戏 DLL）。编译本文件 + TransportKindRules.cs。
    /// </summary>
    public static class Program
    {
        private static int s_pass;
        private static int s_fail;

        private static void Check(bool cond, string name)
        {
            if (cond) { s_pass++; System.Console.WriteLine("PASS " + name); }
            else { s_fail++; System.Console.WriteLine("FAIL " + name); }
        }

        public static int Main()
        {
            // 类型支持范围（v0.5.0：公交 + 有轨电车）
            Check(TransportKindRules.IsLazyModeSupported(TransportKind.Bus), "bus supported");
            Check(TransportKindRules.IsLazyModeSupported(TransportKind.Tram), "tram supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.None), "none not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Train), "train not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Subway), "subway not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Ship), "ship not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Airplane), "airplane not supported");

            // v0.6.0 需求 3：懒人模式不再绑定"当前线路工具的类别"，玩家点哪类站台资产就用哪类。
            // v0.9.0 需求 4：电车那一类从懒人模式里剥出来了 —— 分类仍然认得电车站（上面那组不变），
            // 但"能不能拿它跑懒人模式 / 能不能选它换类别"改由 IsLazyModeEnabled(…, tramEnabled) 说话。
            Check(TransportKindRules.IsLazyModeEnabled(TransportKind.Bus, false), "bus enabled even with tram off");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.Tram, false), "tram gated off in 0.9.0");
            Check(TransportKindRules.IsLazyModeEnabled(TransportKind.Tram, true), "tram enables again when the option opens");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.Train, true), "train never enabled");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.None, true), "none never enabled");

            Check(TransportKindRules.IsSelectableDuringLazy(TransportKind.Bus, false), "bus asset selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Tram, false), "tram not selectable while gated");
            Check(TransportKindRules.IsSelectableDuringLazy(TransportKind.Tram, true), "tram selectable when opened");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.None, true), "none not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Train, true), "train asset not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Subway, true), "subway not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Airplane, true), "airplane not selectable");

            Check(TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Bus), "bus blocks bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Tram), "tram not block bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.None), "none not block");
            Check(TransportKindRules.BlocksNewStation(TransportKind.Tram, TransportKind.Tram), "tram blocks tram");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Tram, TransportKind.Bus), "bus not block tram");

            // 需求 5：退出策略只剩两项，且只有"删本次自动落地的站台"这一种删除，绝不删停靠点。
            Check(!TransportKindRules.ShouldDeleteSessionStops(ExitDeletePolicy.KeepStops), "keep default");
            Check(TransportKindRules.ShouldDeleteSessionStops(ExitDeletePolicy.DeleteSessionStops), "delete session stops");
            Check((int)ExitDeletePolicy.KeepStops == 0, "keep is 0（老存档里的 0 仍然读作保留）");
            Check((int)ExitDeletePolicy.DeleteSessionStops == 1, "delete is 1");
            Check(System.Enum.GetValues(typeof(ExitDeletePolicy)).Length == 2, "下拉只有 2 项");


            // v0.6.0 删掉了账本（PlacedStopLedger）：不再"给站台补缓冲 / 事后回填停靠点"，
            // 站台与线路的关系完全交回原版，所以这里没有账本可测了。

            // ===== BLG key 形状（BridgeTheLanguageGap Scope）=====
            const string inst = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";
            string pk = BlgKeys.PanelKey("WARN_SELECT_STATION");
            Check(pk.StartsWith("Options.", System.StringComparison.Ordinal), "panel key Options prefix");
            Check(BlgKeys.ExtractIdentifier(pk) != null
                  && BlgKeys.ExtractIdentifier(pk).StartsWith(inst, System.StringComparison.Ordinal),
                  "panel ident starts with setting.id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, inst), "panel as ModOptions full id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, "BusLineAutoStops"), "panel as ModOptions short id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, "BusLineAutoStops.BusLineAutoStops"), "panel as ModOptions ns id");

            string ak = BlgKeys.PanelActionKey("WARN_SELECT_STATION");
            Check(ak.StartsWith("Common.ACTION[", System.StringComparison.Ordinal), "action key prefix");
            Check(BlgKeys.WouldClassifyAsModOptions(ak, inst), "action as ModOptions");

            string ek = BlgKeys.EnumKey("ExitDeletePolicy", "KeepStops");
            Check(ek.StartsWith("Options." + inst + ".ExitDeletePolicy[", System.StringComparison.Ordinal),
                  "enum key shape");
            // 实测/推演：枚举成员 key 的 [] 内只有成员名（KeepStops），
            // BLG ExtractIdentifier 拿不到 setting.id ⇒ 归 GameCore，不是 ModOptions。
            // 下拉文案仍经 Translate（可被 BLG 的「游戏本体」范围翻译）。
            Check(!BlgKeys.WouldClassifyAsModOptions(ek, inst), "enum member is GameCore not ModOptions");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, inst), "panel still ModOptions");

            Check(!BlgKeys.WouldClassifyAsModOptions("Assets.NAME[x]", inst), "asset name not ModOptions");
            Check(!BlgKeys.WouldClassifyAsModOptions("Options.X[OtherMod]", inst), "other mod not ours");
            Check(!BlgKeys.WouldClassifyAsModOptions("Hello world", inst), "bare text not ModOptions");

            System.Console.WriteLine("TOTAL pass=" + s_pass + " fail=" + s_fail);
            return s_fail == 0 ? 0 : 1;
        }
    }
}

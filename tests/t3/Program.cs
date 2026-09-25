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
            // 类型支持范围
            Check(TransportKindRules.IsLazyModeSupported(TransportKind.Bus), "bus supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Tram), "tram not yet");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.None), "none not supported");

            Check(TransportKindRules.CanSelectDuringLazy(TransportKind.Bus, TransportKind.Bus), "same bus ok");
            Check(!TransportKindRules.CanSelectDuringLazy(TransportKind.Bus, TransportKind.Tram), "cross kind blocked");
            Check(!TransportKindRules.CanSelectDuringLazy(TransportKind.None, TransportKind.Bus), "no mode");

            Check(TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Bus), "bus blocks bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Tram), "tram not block bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.None), "none not block");

            // 删除策略
            var n = TransportKindRules.ResolveExitDelete(ExitDeletePolicy.DoNothing);
            Check(!n.DeleteStops && !n.DeleteStations, "policy nothing");
            var a = TransportKindRules.ResolveExitDelete(ExitDeletePolicy.DeleteStopsAndStations);
            Check(a.DeleteStops && a.DeleteStations, "policy all");
            var st = TransportKindRules.ResolveExitDelete(ExitDeletePolicy.DeleteStationsOnly);
            Check(!st.DeleteStops && st.DeleteStations, "policy stations");
            var sp = TransportKindRules.ResolveExitDelete(ExitDeletePolicy.DeleteStopsOnly);
            Check(sp.DeleteStops && !sp.DeleteStations, "policy stops");

            // 账本
            var ledger = new PlacedStopLedger();
            ledger.Track("s1", "p1", TransportKind.Bus, true);
            ledger.Track("s2", "p2", TransportKind.Bus, false); // 预存站台关联，非本模组生成
            Check(ledger.Count == 2, "ledger count");
            Check(ledger.ShouldDeleteStationWithStop("s1"), "byThisMod delete");
            Check(!ledger.ShouldDeleteStationWithStop("s2"), "not byThisMod keep");
            Check(!ledger.ShouldDeleteStationWithStop("missing"), "missing no delete");
            Check(ledger.GetStationKey("s1") == "p1", "station key");

            var stops = new List<string>();
            var stations = new List<string>();
            ledger.CollectExitDeletes(ExitDeletePolicy.DeleteStopsAndStations, stops, stations);
            Check(stops.Count == 1 && stations.Count == 1, "exit collect only byThisMod");

            stops.Clear(); stations.Clear();
            ledger.CollectExitDeletes(ExitDeletePolicy.DoNothing, stops, stations);
            Check(stops.Count == 0 && stations.Count == 0, "exit nothing");

            Check(ledger.Remove("s1"), "remove");
            Check(!ledger.ShouldDeleteStationWithStop("s1"), "removed");

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

            string ek = BlgKeys.EnumKey("ExitDeletePolicy", "DoNothing");
            Check(ek.StartsWith("Options." + inst + ".ExitDeletePolicy[", System.StringComparison.Ordinal),
                  "enum key shape");
            // 实测/推演：枚举成员 key 的 [] 内只有成员名（DoNothing），
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

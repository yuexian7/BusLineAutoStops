using System;
using System.Collections.Generic;
using Colossal;
using Game.Modding;

namespace BusLineAutoStops
{
    /// <summary>
    /// 12 官方语言词条。每份字典按传入 locale 构建（禁用全局 activeLocale — Playbook §3.3）。
    /// 同时提供游戏内面板 key（经 UILocalizationManager.Translate，BridgeTheLanguageGap 可翻）。
    /// </summary>
    public static class LocaleTable
    {
        public static readonly string[] kLocales =
        {
            "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP",
            "ko-KR", "pl-PL", "pt-BR", "ru-RU", "zh-HANS", "zh-HANT"
        };

        private static string _activeLocale = "en-US";

        public static void SetActiveLocale(string locale)
        {
            for (int i = 0; i < kLocales.Length; i++)
            {
                if (string.Equals(kLocales[i], locale, StringComparison.OrdinalIgnoreCase))
                {
                    _activeLocale = kLocales[i];
                    return;
                }
            }
            _activeLocale = "en-US";
        }

        /// <summary>下拉 displayName / 运行时短词。</summary>
        public static string T(string key)
        {
            Dictionary<string, string> table = Build(_activeLocale);
            string v;
            if (table.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            Dictionary<string, string> en = Build("en-US");
            return en.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>给 IDictionarySource：设置页完整 key 映射。</summary>
        public static Dictionary<string, string> BuildForSetting(ModSetting setting, string locale)
        {
            Dictionary<string, string> d = Build(locale);
            Dictionary<string, string> en = Build("en-US");
            // 缺键回退英文，避免 Put 抛 KeyNotFound
            foreach (var kv in en)
            {
                if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
            }
            var map = new Dictionary<string, string>();
            Put(map, setting.GetSettingsLocaleID(), d["mod.name"]);
            Put(map, setting.GetOptionTabLocaleID(Setting.kTabGeneral), d["tab.general"]);
            Put(map, setting.GetOptionGroupLocaleID(Setting.kGroupMain), d["group.main"]);
            Put(map, setting.GetOptionGroupLocaleID(Setting.kGroupAbout), d["group.about"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.ExitPolicy)), d["policy.label"]);
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.ExitPolicy)), d["policy.desc"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.ToggleLazyBinding)), d["hotkey.toggle"]);
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.ToggleLazyBinding)), d["hotkey.toggle.desc"]);
            Put(map, setting.GetBindingMapLocaleID(), d["binding.map"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.Version)), d["version.label"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.Author)), d["author.label"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenKoFi)), d["link.kofi"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenForum)), d["link.forum"]);
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenRainbow)), d["link.rainbow"]);

            // 枚举下拉成员：框架 key + 手写模板双注册（键相同则只写一份）
            Put(map, setting.GetEnumValueLocaleID(ExitDeletePolicy.DoNothing), d["policy.nothing"]);
            Put(map, setting.GetEnumValueLocaleID(ExitDeletePolicy.DeleteStopsAndStations), d["policy.all"]);
            Put(map, setting.GetEnumValueLocaleID(ExitDeletePolicy.DeleteStationsOnly), d["policy.stations"]);
            Put(map, setting.GetEnumValueLocaleID(ExitDeletePolicy.DeleteStopsOnly), d["policy.stops"]);
            Put(map, Setting.ExitPolicyEnumKey(ExitDeletePolicy.DoNothing), d["policy.nothing"]);
            Put(map, Setting.ExitPolicyEnumKey(ExitDeletePolicy.DeleteStopsAndStations), d["policy.all"]);
            Put(map, Setting.ExitPolicyEnumKey(ExitDeletePolicy.DeleteStationsOnly), d["policy.stations"]);
            Put(map, Setting.ExitPolicyEnumKey(ExitDeletePolicy.DeleteStopsOnly), d["policy.stops"]);
            return map;
        }

        private static void Put(Dictionary<string, string> map, string key, string value)
        {
            if (string.IsNullOrEmpty(key) || value == null) return;
            map[key] = value;
        }

        /// <summary>
        /// 游戏内面板词条 key（Options 形态）。走 UILocalizationManager.Translate ⇒ BridgeTheLanguageGap 可翻。
        /// 详见 BlgKeys：identifier 必须以 ModSetting.instances 的 key 为前缀。
        /// </summary>
        public static string PanelKey(string slug)
        {
            return BlgKeys.PanelKey(slug);
        }

        public static Dictionary<string, string> BuildPanelMap(string locale)
        {
            Dictionary<string, string> d = Build(locale);
            string[] slugs = { "WARN_SELECT_STATION", "BTN_TOGGLE", "MODE_ON", "MODE_OFF", "WARN_NOT_BUS" };
            string[] vals =
            {
                d["warn.selectStation"], d["btn.toggle"], d["mode.on"], d["mode.off"], d["warn.notBus"]
            };
            var map = new Dictionary<string, string>();
            for (int i = 0; i < slugs.Length; i++)
            {
                map[BlgKeys.PanelKey(slugs[i])] = vals[i];
                map[BlgKeys.PanelActionKey(slugs[i])] = vals[i];
            }
            return map;
        }

        public static Dictionary<string, string> Build(string locale)
        {
            switch (locale)
            {
                case "de-DE": return De();
                case "es-ES": return Es();
                case "fr-FR": return Fr();
                case "it-IT": return It();
                case "ja-JP": return Ja();
                case "ko-KR": return Ko();
                case "pl-PL": return Pl();
                case "pt-BR": return Pt();
                case "ru-RU": return Ru();
                case "zh-HANS": return ZhHans();
                case "zh-HANT": return ZhHant();
                default: return En();
            }
        }

        private static Dictionary<string, string> En()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "General" },
                { "group.main", "Behaviour" },
                { "group.about", "About" },
                { "policy.label", "When exiting lazy mode mid-way" },
                { "policy.desc", "What to delete if you turn lazy mode off before finishing the line." },
                { "policy.nothing", "Do nothing" },
                { "policy.all", "Delete new stops and stations" },
                { "policy.stations", "Delete new stations only" },
                { "policy.stops", "Delete new stops only" },
                { "hotkey.toggle", "Toggle lazy mode" },
                { "hotkey.toggle.desc", "Turn auto station placement on or off. Leave unbound if unused." },
                { "binding.map", "BusLine AutoStops key bindings" },
                { "version.label", "Mod version" },
                { "author.label", "Author" },
                { "link.kofi", "Buy me a coffee" },
                { "link.forum", "Forum thread" },
                { "link.rainbow", "RAINBOW website" },
                { "warn.selectStation", "Select a bus stop asset!" },
                { "warn.notBus", "Only bus stop assets are supported." },
                { "btn.toggle", "Lazy mode: auto-place stop assets" },
                { "mode.on", "ON" },
                { "mode.off", "OFF" },
            };
        }

        private static Dictionary<string, string> ZhHans()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "公交线路自动站台 BusLine AutoStops" },
                { "tab.general", "常规" },
                { "group.main", "行为" },
                { "group.about", "关于" },
                { "policy.label", "中途退出懒人模式的删除机制" },
                { "policy.desc", "线路未完成就关闭懒人模式时，如何处理本次新建内容。" },
                { "policy.nothing", "不做任何删除" },
                { "policy.all", "删除新建线路全部站台和停靠点" },
                { "policy.stations", "仅删除新建线路站台" },
                { "policy.stops", "仅删除新建线路停靠点" },
                { "hotkey.toggle", "开启/关闭懒人模式" },
                { "hotkey.toggle.desc", "切换自动放置公交站。不使用可留空。" },
                { "binding.map", "公交线路自动站台按键" },
                { "version.label", "模组版本" },
                { "author.label", "作者" },
                { "link.kofi", "请我喝杯咖啡" },
                { "link.forum", "论坛页面" },
                { "link.rainbow", "RAINBOW官网" },
                { "warn.selectStation", "请选择公交站资产!" },
                { "warn.notBus", "目前仅支持公交站资产。" },
                { "btn.toggle", "懒人模式：自动放置公交站" },
                { "mode.on", "开" },
                { "mode.off", "关" },
            };
        }

        private static Dictionary<string, string> ZhHant()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "公車路線自動月台 BusLine AutoStops" },
                { "tab.general", "一般" },
                { "policy.label", "中途離開懶人模式的刪除機制" },
                { "policy.desc", "路線未完成就關閉懶人模式時，如何處理本次新增內容。" },
                { "policy.nothing", "不做任何刪除" },
                { "policy.all", "刪除新建路線全部月台和停靠點" },
                { "policy.stations", "僅刪除新建路線月台" },
                { "policy.stops", "僅刪除新建路線停靠點" },
                { "hotkey.toggle", "開啟/關閉懶人模式" },
                { "hotkey.toggle.desc", "切換自動放置月台。不使用可留空。" },
                { "binding.map", "公車路線自動月台" },
                { "warn.selectStation", "請選擇月台資產!" },
                { "warn.notBus", "目前僅支援公車月台。" },
                { "btn.toggle", "懶人模式：自動放置月台" },
                { "mode.on", "開" },
                { "mode.off", "關" },
            };
        }

        private static Dictionary<string, string> De()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "Allgemein" },
                { "policy.label", "Beim vorzeitigen Verlassen des Komfortmodus" },
                { "policy.desc", "Was geloescht wird, wenn der Komfortmodus vor Linienende ausgeschaltet wird." },
                { "policy.nothing", "Nichts loeschen" },
                { "policy.all", "Neue Halte und Stationen loeschen" },
                { "policy.stations", "Nur neue Stationen loeschen" },
                { "policy.stops", "Nur neue Halte loeschen" },
                { "hotkey.toggle", "Komfortmodus umschalten" },
                { "hotkey.toggle.desc", "Automatische Stationsplatzierung an/aus. Kann leer bleiben." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Bitte eine Stationsassets auswaehlen!" },
                { "warn.notBus", "Derzeit nur Bushaltestellen." },
                { "btn.toggle", "Komfortmodus: Stationen automatisch" },
                { "mode.on", "AN" },
                { "mode.off", "AUS" },
            };
        }

        private static Dictionary<string, string> Es()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "General" },
                { "policy.label", "Al salir del modo facil a mitad" },
                { "policy.desc", "Que se borra si apagas el modo facil antes de terminar la linea." },
                { "policy.nothing", "No borrar nada" },
                { "policy.all", "Borrar nuevas paradas y estaciones" },
                { "policy.stations", "Borrar solo nuevas estaciones" },
                { "policy.stops", "Borrar solo nuevas paradas" },
                { "hotkey.toggle", "Alternar modo facil" },
                { "hotkey.toggle.desc", "Colocacion automatica de estaciones. Puede quedar vacio." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Seleccione un activo de estacion!" },
                { "warn.notBus", "Solo paradas de autobus por ahora." },
                { "btn.toggle", "Modo facil: estaciones auto" },
                { "mode.on", "ON" },
                { "mode.off", "OFF" },
            };
        }

        private static Dictionary<string, string> Fr()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "General" },
                { "policy.label", "En quittant le mode facile en cours" },
                { "policy.desc", "Que supprimer si vous coupez le mode facile avant la fin de la ligne." },
                { "policy.nothing", "Ne rien supprimer" },
                { "policy.all", "Supprimer arrets et stations crees" },
                { "policy.stations", "Supprimer seulement les stations" },
                { "policy.stops", "Supprimer seulement les arrets" },
                { "hotkey.toggle", "Basculer le mode facile" },
                { "hotkey.toggle.desc", "Placement automatique des stations. Peut rester vide." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Veuillez choisir un actif de station !" },
                { "warn.notBus", "Seulement les stations de bus pour l'instant." },
                { "btn.toggle", "Mode facile : stations auto" },
                { "mode.on", "ON" },
                { "mode.off", "OFF" },
            };
        }

        private static Dictionary<string, string> It()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "Generale" },
                { "policy.label", "Uscita anticipata dalla modalita facile" },
                { "policy.desc", "Cosa eliminare se disattivi la modalita facile prima di finire la linea." },
                { "policy.nothing", "Non eliminare nulla" },
                { "policy.all", "Elimina nuove fermate e stazioni" },
                { "policy.stations", "Elimina solo nuove stazioni" },
                { "policy.stops", "Elimina solo nuove fermate" },
                { "hotkey.toggle", "Attiva/disattiva modalita facile" },
                { "hotkey.toggle.desc", "Posizionamento automatico stazioni. Puoi lasciare vuoto." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Seleziona un asset stazione!" },
                { "warn.notBus", "Solo fermate autobus per ora." },
                { "btn.toggle", "Modalita facile: stazioni auto" },
                { "mode.on", "ON" },
                { "mode.off", "OFF" },
            };
        }

        private static Dictionary<string, string> Ja()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "一般" },
                { "policy.label", "途中でかんたんモードを終了した場合" },
                { "policy.desc", "路線完成前にかんたんモードを切ったときの削除内容。" },
                { "policy.nothing", "削除しない" },
                { "policy.all", "新しい停留所とステーションを削除" },
                { "policy.stations", "新しいステーションのみ削除" },
                { "policy.stops", "新しい停留所のみ削除" },
                { "hotkey.toggle", "かんたんモード切替" },
                { "hotkey.toggle.desc", "ステーション自動設置のオン/オフ。未使用なら空欄。" },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "ステーション資産を選択してください!" },
                { "warn.notBus", "現在バスステーションのみ対応。" },
                { "btn.toggle", "かんたんモード：ステーション自動" },
                { "mode.on", "オン" },
                { "mode.off", "オフ" },
            };
        }

        private static Dictionary<string, string> Ko()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "일반" },
                { "policy.label", "간편 모드 중간 종료 시" },
                { "policy.desc", "노선 완성 전 간편 모드를 끌 때 삭제할 대상입니다." },
                { "policy.nothing", "삭제 안 함" },
                { "policy.all", "새 정류장·역 삭제" },
                { "policy.stations", "새 역만 삭제" },
                { "policy.stops", "새 정류장만 삭제" },
                { "hotkey.toggle", "간편 모드 전환" },
                { "hotkey.toggle.desc", "역 자동 배치 켜기/끄기. 미사용 시 비워 두세요." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "역 에셋을 선택하세요!" },
                { "warn.notBus", "현재 버스 정류장만 지원합니다." },
                { "btn.toggle", "간편 모드: 역 자동 배치" },
                { "mode.on", "켬" },
                { "mode.off", "끔" },
            };
        }

        private static Dictionary<string, string> Pl()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "Ogolne" },
                { "policy.label", "Przedwczesne wyjscie z trybu latwego" },
                { "policy.desc", "Co usunac, jesli wylaczysz tryb latwy przed koncem linii." },
                { "policy.nothing", "Nic nie usuwaj" },
                { "policy.all", "Usun nowe przystanki i stacje" },
                { "policy.stations", "Usun tylko nowe stacje" },
                { "policy.stops", "Usun tylko nowe przystanki" },
                { "hotkey.toggle", "Przelacz tryb latwy" },
                { "hotkey.toggle.desc", "Auto-stawianie stacji. Moze zostac puste." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Wybierz zasob stacji!" },
                { "warn.notBus", "Na razie tylko przystanki autobusowe." },
                { "btn.toggle", "Tryb latwy: stacje auto" },
                { "mode.on", "WL" },
                { "mode.off", "WYL" },
            };
        }

        private static Dictionary<string, string> Pt()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "Geral" },
                { "policy.label", "Ao sair do modo facil no meio" },
                { "policy.desc", "O que apagar se desligar o modo facil antes de terminar a linha." },
                { "policy.nothing", "Nao apagar nada" },
                { "policy.all", "Apagar novas paradas e estacoes" },
                { "policy.stations", "Apagar so novas estacoes" },
                { "policy.stops", "Apagar so novas paradas" },
                { "hotkey.toggle", "Alternar modo facil" },
                { "hotkey.toggle.desc", "Colocacao automatica de estacoes. Pode ficar vazio." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Selecione um ativo de estacao!" },
                { "warn.notBus", "Apenas paradas de onibus por enquanto." },
                { "btn.toggle", "Modo facil: estacoes auto" },
                { "mode.on", "LIG" },
                { "mode.off", "DES" },
            };
        }

        private static Dictionary<string, string> Ru()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "Общие" },
                { "policy.label", "При досрочном выходе из режима" },
                { "policy.desc", "Что удалить, если выключить режим до конца маршрута." },
                { "policy.nothing", "Ничего не удалять" },
                { "policy.all", "Удалить новые остановки и станции" },
                { "policy.stations", "Удалить только новые станции" },
                { "policy.stops", "Удалить только новые остановки" },
                { "hotkey.toggle", "Переключить режим" },
                { "hotkey.toggle.desc", "Автоустановка станций. Можно оставить пустым." },
                { "binding.map", "BusLine AutoStops" },
                { "warn.selectStation", "Выберите объект станции!" },
                { "warn.notBus", "Пока только автобусные остановки." },
                { "btn.toggle", "Режим: автостанции" },
                { "mode.on", "ВКЛ" },
                { "mode.off", "ВЫКЛ" },
            };
        }
    }

    /// <summary>设置页语言源。</summary>
    public sealed class SettingLocaleSource : IDictionarySource
    {
        private readonly ModSetting _setting;
        private readonly string _locale;

        public SettingLocaleSource(ModSetting setting, string locale)
        {
            _setting = setting;
            _locale = locale;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return LocaleTable.BuildForSetting(_setting, _locale);
        }

        public void Unload() { }
    }

    /// <summary>游戏内面板语言源（经 Translate，BLG 可翻）。</summary>
    public sealed class PanelLocaleSource : IDictionarySource
    {
        private readonly string _locale;

        public PanelLocaleSource(string locale)
        {
            _locale = locale;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return LocaleTable.BuildPanelMap(_locale);
        }

        public void Unload() { }
    }
}

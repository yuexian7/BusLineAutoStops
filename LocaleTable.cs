using System;
using System.Collections.Generic;
using Colossal;
using Game.Modding;

namespace BusLineAutoStops
{
    /// <summary>
    /// 12 官方语言词条。每份字典按传入 locale 构建（禁用全局 activeLocale — Playbook §3.3）。
    /// 同时提供游戏内面板 key（经 UILocalizationManager.Translate，BridgeTheLanguageGap 可翻）。
    ///
    /// 文案约定（v0.5.0 需求 4 全量重做）：
    /// - **术语按原版语言包逐条核对**（Content/Game/Locale.cok，12 locale 全打过）：
    ///   Assets.NAME[Bus Stop] / Assets.NAME[Tram Stop] 给出资产名 ——
    ///   公交站/有轨电车站（zh-HANS）、公車站/電車站（zh-HANT）、Bus Stop/Tram Stop（en-US）、
    ///   Bushaltestelle/Straßenbahnhaltestelle、Parada de autobús/de tranvía、Arrêt de bus/de tramway、
    ///   Fermata dell'autobus/dei tram、Ponto de ônibus/bonde、Przystanek autobusowy/tramwajowy、
    ///   Автобусная/Трамвайная остановка、버스/전차 정류장、バス停/路面電車停留所。
    ///   Common.ACTION[Remove Waypoint] 给出 waypoint 名 —— 路径点/路徑點、Wegpunkt、
    ///   węzeł trasy、punkt de passagem、tappa intermedia、通過地点、웨이포인트、 punto、点…
    ///   旧版中文一律写「站台」是**错的**：原版「站台」指车站月台（platform），
    ///   我们放的东西是公交站/有轨电车站资产；「停靠点」也不是原版 waypoint 的中文说法。
    /// - 需要统称时用「站点 / 站點 / stop / Haltestelle / parada / arrêt / fermata / ponto /
    ///   przystanek / остановка / 정류장 / 停留所」，不去用「站台」。
    /// - 只用玩家看得懂、且本模组真能做到的词：懒人模式支持公交与有轨电车两类线路
    ///   （TransportKindRules.IsLazyModeSupported），其余交通类型走 WARN_UNSUPPORTED_LINE。
    /// - tooltip 一律短名词短语，提示语一句话说清；所有取值走 Get()，缺键回退英文，
    ///   绝不让 KeyNotFound 冒到运行时。
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
            return Get(Build(_activeLocale), Build("en-US"), key);
        }

        /// <summary>取词条，缺键回退英文。</summary>
        private static string Get(Dictionary<string, string> d, Dictionary<string, string> en, string key)
        {
            string v;
            if (d.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return en.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>给 IDictionarySource：设置页完整 key 映射。</summary>
        public static Dictionary<string, string> BuildForSetting(ModSetting setting, string locale)
        {
            Dictionary<string, string> d = Build(locale);
            Dictionary<string, string> en = Build("en-US");
            var map = new Dictionary<string, string>();
            Put(map, setting.GetSettingsLocaleID(), Get(d, en, "mod.name"));
            Put(map, setting.GetOptionTabLocaleID(Setting.kTabGeneral), Get(d, en, "tab.general"));
            Put(map, setting.GetOptionGroupLocaleID(Setting.kGroupMain), Get(d, en, "group.main"));
            Put(map, setting.GetOptionGroupLocaleID(Setting.kGroupAbout), Get(d, en, "group.about"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.AutoPlaceEnabled)), Get(d, en, "master.label"));
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.AutoPlaceEnabled)), Get(d, en, "master.desc"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.ExitPolicy)), Get(d, en, "policy.label"));
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.ExitPolicy)), Get(d, en, "policy.desc"));
            // 需求 4（0.9.0）：有轨电车从懒人模式里剥出来单列一条，本版置灰不可选 + 注明暂未上线。
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.TramEnabled)), Get(d, en, "tram.label"));
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.TramEnabled)), Get(d, en, "tram.desc"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.ToggleLazyBinding)), Get(d, en, "hotkey.toggle"));
            Put(map, setting.GetOptionDescLocaleID(nameof(Setting.ToggleLazyBinding)), Get(d, en, "hotkey.toggle.desc"));
            Put(map, setting.GetBindingMapLocaleID(), Get(d, en, "binding.map"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.Version)), Get(d, en, "version.label"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.Author)), Get(d, en, "author.label"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenKoFi)), Get(d, en, "link.kofi"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenForum)), Get(d, en, "link.forum"));
            Put(map, setting.GetOptionLabelLocaleID(nameof(Setting.OpenRainbow)), Get(d, en, "link.rainbow"));

            // 枚举下拉成员：框架 key + 手写模板双注册（BLG 的 EnumKey 模板要的是后者）
            // 需求 5（v0.6.0）：只有两项 —— 保留公交站 / 不保留公交站。
            // 线路停靠点在中途退出时本来就不保留（原版行为），我们不去对抗，也就没有"删停靠点"这种选项。
            PutEnum(map, setting, ExitDeletePolicy.KeepStops, Get(d, en, "policy.keep"));
            PutEnum(map, setting, ExitDeletePolicy.DeleteSessionStops, Get(d, en, "policy.delete"));

            return map;
        }

        private static void PutEnum(Dictionary<string, string> map, ModSetting setting, ExitDeletePolicy p, string value)
        {
            Put(map, setting.GetEnumValueLocaleID(p), value);
            Put(map, Setting.ExitPolicyEnumKey(p), value);
        }

        private static void Put(Dictionary<string, string> map, string key, string value)
        {
            if (string.IsNullOrEmpty(key) || value == null) return;
            map[key] = value;
        }

        /// <summary>
        /// 游戏内面板词条 key（Options 形态）。走 UILocalizationManager.Translate ⇒ BLG 可拦。
        /// 前端用到六个 slug：按钮 tooltip + 五条提示（没资产 / 这类线路不管 / 电车暂未上线 /
        /// 这里放不下 / 总开关关了）。
        /// </summary>
        public static string PanelKey(string slug)
        {
            return BlgKeys.PanelKey(slug);
        }

        public static Dictionary<string, string> BuildPanelMap(string locale)
        {
            Dictionary<string, string> d = Build(locale);
            Dictionary<string, string> en = Build("en-US");
            // v0.5.0 需求 4：WARN_NOT_BUS → WARN_UNSUPPORTED_LINE（有轨电车也支持了，
            // 这条提示的含义变成"这类线路本模组不管"）。
            string[] slugs = { "BTN_TOGGLE", "WARN_SELECT_STATION", "WARN_UNSUPPORTED_LINE", "WARN_TRAM_OFF", "WARN_NO_PLACE", "WARN_MASTER_OFF" };
            string[] keys = { "btn.toggle", "warn.selectStation", "warn.unsupportedLine", "warn.tramNotOnline", "warn.noPlace", "warn.masterOff" };
            var map = new Dictionary<string, string>();
            for (int i = 0; i < slugs.Length; i++)
            {
                string v = Get(d, en, keys[i]);
                map[BlgKeys.PanelKey(slugs[i])] = v;
                map[BlgKeys.PanelActionKey(slugs[i])] = v;
            }
            return map;
        }

        /// <summary>
        /// 取某语言的词条表（**按语言缓存**，需求 5 v0.10.0 的性能审查）。
        ///
        /// 为什么要缓存：`SettingLocaleSource.ReadEntries` 与面板语言源每次被游戏问到都要拿这张表，
        /// 而旧写法每问一次就 `new Dictionary` 建两份 30 键的表（当前语言 + 英文回退）。
        /// 词条表本身是不可变的，缓存之后调用方只读，不再重复构造。
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> s_cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, string> Build(string locale)
        {
            Dictionary<string, string> hit;
            if (s_cache.TryGetValue(locale ?? "en-US", out hit)) return hit;
            Dictionary<string, string> d = BuildFresh(locale);
            s_cache[locale ?? "en-US"] = d;
            return d;
        }

        private static Dictionary<string, string> BuildFresh(string locale)
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

        // ── 词条表：所有语言键集一致（24 键） ──────────────────────────────────────────
        private const string K = "mod.name";

        private static Dictionary<string, string> En()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "BusLine AutoStops" },
                { "tab.general", "General" },
                { "group.main", "Lazy mode" },
                { "group.about", "About" },
                { "master.label", "Enable automatic bus stop placement" },
                { "master.desc", "Lazy mode works inside the line tool. While drawing a bus line, hold the cursor still for about 0.2 s and the game shows a preview bus stop on the pavement; your own left click is what places it, and the waypoint attaches to that stop. Nothing is ever placed automatically and the mod never presses buttons for you; a spot the game refuses shows no preview. With lazy mode on, the transport panel lists bus stop assets only." },
                { "policy.label", "When lazy mode ends" },
                { "policy.desc", "Only covers bus stops this mod placed automatically during this session; a stop that a line's waypoints already use is always kept. Cancelling an unfinished line is vanilla behaviour - the game drops those waypoints itself and this mod does not get involved." },
                { "tram.label", "Tram stops (not available yet)" },
                { "tram.desc", "Tram stops follow different placement rules than bus stops (a tram line can only be drawn from tram stop to tram stop), so this part is not finished yet. The option stays here, greyed out, and a later update unlocks it." },
                { "warn.tramNotOnline", "Tram stops are not available in this version - lazy mode works on bus lines only." },
                { "policy.keep", "Keep the bus stops" },
                { "policy.delete", "Remove the bus stops" },
                { "hotkey.toggle", "Toggle lazy mode" },
                { "hotkey.toggle.desc", "Turns lazy mode (bus stop preview) on or off." },
                { "binding.map", "BusLine AutoStops" },
                { "version.label", "Version" },
                { "author.label", "Author" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Forum thread" },
                { "link.rainbow", "RAINBOW site" },
                { "btn.toggle", "Auto-place bus and tram stops" },
                { "warn.selectStation", "No bus stop asset available" },
                { "warn.unsupportedLine", "Only bus lines work with this mod." },
                { "warn.noPlace", "No room for a stop here" },
                { "warn.masterOff", "Automatic stop placement is off in settings" },
            };
        }

        private static Dictionary<string, string> ZhHans()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "公交站自动放置" },
                { "tab.general", "常规" },
                { "group.main", "懒人模式" },
                { "group.about", "关于" },
                { "master.label", "开启公交站自动放置" },
                { "master.desc", "懒人模式挂在创建线路工具里。画公交线路时把光标停住约 0.2 秒，原版会在人行道上给出公交站的预览；真正放下的是你自己的左键，路径点会同时依附到那枚公交站。整个过程没有任何自动放置，模组也不会替你按键；原版不给放的位置不出现预览。懒人模式开着时，交通面板只列公交站资产。" },
                { "policy.label", "退出懒人模式时" },
                { "policy.desc", "只管本次会话里由模组自动落地的公交站；已经被线路路径点用上的站点永远保留。线路没画完就取消是原版行为 —— 路径点由游戏自己丢弃，本模组不参与、也不对抗。" },
                { "tram.label", "有轨电车站自动放置（暂未上线）" },
                { "tram.desc", "有轨电车站的落点规则和公交站不一样（电车线路只能从电车站连到电车站），这部分本版还没做完。条目先保留并置灰，后续版本开放。" },
                { "warn.tramNotOnline", "有轨电车站暂未上线，懒人模式只处理公交线路。" },
                { "policy.keep", "保留公交站" },
                { "policy.delete", "不保留公交站" },
                { "hotkey.toggle", "切换懒人模式" },
                { "hotkey.toggle.desc", "开启或关闭懒人模式（公交站预览）。" },
                { "binding.map", "公交站自动放置" },
                { "version.label", "版本" },
                { "author.label", "作者" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "论坛帖子" },
                { "link.rainbow", "RAINBOW 官网" },
                { "btn.toggle", "自动放置公交站／有轨电车站" },
                { "warn.selectStation", "没有可用的公交站资产" },
                { "warn.unsupportedLine", "本模组只处理公交线路。" },
                { "warn.noPlace", "这里放不下站点" },
                { "warn.masterOff", "设置里已关闭公交站自动放置" },
            };
        }

        private static Dictionary<string, string> ZhHant()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "公車站自動放置" },
                { "tab.general", "一般" },
                { "group.main", "懶人模式" },
                { "group.about", "關於" },
                { "master.label", "開啟公車站自動放置" },
                { "master.desc", "懶人模式掛在建立路線工具裡。畫公交路線時把光標停住約 0.2 秒，原版會在人行道上給出公車站預覽；真正放下的是你自己的左鍵，路徑點同時依附上下那個站。整個過程沒有自動放置，模組也不會替你按鍵；原版不給放的位置不會出現預覽。懶人模式開啟時，交通面板只列公車站資產。" },
                { "policy.label", "退出懶人模式時" },
                { "policy.desc", "只處理本次會話裡由模組自動落地的公車站；已被路線路徑點使用的站點永遠保留。路線沒畫完就取消是原版行為 —— 路徑點由遊戲自行捨棄，本模組不干預、也不對抗。" },
                { "tram.label", "電車站自動放置（暫未上線）" },
                { "tram.desc", "電車站的落點規則與公車站不同（電車路線只能從電車站連到電車站），這個版本還沒完成。條目先保留並置灰，後續版本開放。" },
                { "warn.tramNotOnline", "電車站暫未上線，懶人模式只處理公交路線。" },
                { "policy.keep", "保留公車站" },
                { "policy.delete", "不保留公車站" },
                { "hotkey.toggle", "切換懶人模式" },
                { "hotkey.toggle.desc", "開啟或關閉懶人模式（公車站預覽）。" },
                { "binding.map", "公車站自動放置" },
                { "version.label", "版本" },
                { "author.label", "作者" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "論壇帖子" },
                { "link.rainbow", "RAINBOW 官網" },
                { "btn.toggle", "自動放置公車站／電車站" },
                { "warn.selectStation", "沒有可用的公車站資產" },
                { "warn.unsupportedLine", "本模組只處理公交路線。" },
                { "warn.noPlace", "這裡放不下站點" },
                { "warn.masterOff", "設定裡已關閉公車站自動放置" },
            };
        }

        private static Dictionary<string, string> De()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Automatische Bushaltestellen" },
                { "tab.general", "Allgemein" },
                { "group.main", "Komfortmodus" },
                { "group.about", "Info" },
                { "master.label", "Automatische Haltestellen platzieren" },
                { "master.desc", "Der Komfortmodus wirkt im Linienwerkzeug. Wenn du eine Buslinie zeichnest und den Cursor etwa 0,2 s ruhig hältst, zeigt das Spiel eine Vorschau der Bushaltestelle am Gehweg; gesetzt wird sie erst durch deinen eigenen Linksklick, und der Wegpunkt hängt sich an diese Haltestelle. Nichts wird automatisch platziert und der Mod drückt niemals Buttons für dich; eine Stelle, die das Spiel ablehnt, zeigt keine Vorschau. Solange der Komfortmodus aktiv ist, listet das Transportpanel nur Bushaltestellen." },
                { "policy.label", "Beim Beenden des Komfortmodus" },
                { "policy.desc", "Betrifft nur Bushaltestellen, die dieser Mod in dieser Sitzung automatisch gesetzt hat; eine Haltestelle, die Wegpunkte einer Linie schon nutzen, bleibt immer erhalten. Eine unvollständige Linie abzubrechen ist Originalverhalten – das Spiel verwirft die Wegpunkte selbst, der Mod mischt sich nicht ein." },
                { "tram.label", "Straßenbahnhaltestellen (noch nicht verfügbar)" },
                { "tram.desc", "Straßenbahnhaltestellen folgen anderen Platzierungsregeln als Bushaltestellen (eine Straßenbahnlinie wird von Haltestelle zu Haltestelle gezeichnet). Dieser Teil ist noch nicht fertig; der Punkt bleibt hier grau reserviert und wird später freigeschaltet." },
                { "warn.tramNotOnline", "Straßenbahnhaltestellen sind noch nicht verfügbar – der Komfortmodus arbeitet nur mit Buslinien." },
                { "policy.keep", "Haltestellen behalten" },
                { "policy.delete", "Haltestellen entfernen" },
                { "hotkey.toggle", "Komfortmodus umschalten" },
                { "hotkey.toggle.desc", "Schaltet den Komfortmodus (Haltestellen-Vorschau) ein oder aus." },
                { "binding.map", "Automatische Bushaltestellen" },
                { "version.label", "Version" },
                { "author.label", "Autor" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Forum-Thread" },
                { "link.rainbow", "RAINBOW-Webseite" },
                { "btn.toggle", "Haltestellen automatisch setzen" },
                { "warn.selectStation", "Keine Bushaltestelle verfügbar" },
                { "warn.unsupportedLine", "Dieser Mod funktioniert nur mit Buslinien." },
                { "warn.noPlace", "Hier ist kein Platz für eine Haltestelle" },
                { "warn.masterOff", "Automatisches Setzen ist in den Einstellungen ausgeschaltet" },
            };
        }

        private static Dictionary<string, string> Es()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Paradas de autobús automáticas" },
                { "tab.general", "General" },
                { "group.main", "Modo fácil" },
                { "group.about", "Acerca de" },
                { "master.label", "Colocación automática de paradas" },
                { "master.desc", "El modo fácil actúa dentro de la herramienta de línea. Mientras trazas una línea de autobús, mantén el cursor quieto unos 0,2 s y el juego muestra una vista previa de la parada en la acera; lo que la coloca es tu propio clic izquierdo, y el punto de la ruta se une a esa parada. Nada se coloca automáticamente y el mod nunca pulsa botones por ti; una posición que el juego rechaza no muestra vista previa. Con el modo fácil activo, el panel de transporte solo lista paradas de autobús." },
                { "policy.label", "Al salir del modo fácil" },
                { "policy.desc", "Solo afecta a las paradas de autobús que este mod colocó automáticamente en esta sesión; una parada que los puntos de una línea ya usan se conserva siempre. Cancelar una línea sin terminar es comportamiento original: el juego descarta sus puntos y este mod no interviene." },
                { "tram.label", "Paradas de tranvía (aún no disponibles)" },
                { "tram.desc", "Las paradas de tranvía siguen reglas distintas a las de autobús (una línea de tranvía se traza de parada en parada). Esta parte aún no está terminada; la opción queda aquí bloqueada y se activará en una actualización futura." },
                { "warn.tramNotOnline", "Las paradas de tranvía aún no están disponibles: el modo fácil solo funciona con líneas de autobús." },
                { "policy.keep", "Conservar las paradas" },
                { "policy.delete", "No conservar las paradas" },
                { "hotkey.toggle", "Alternar modo fácil" },
                { "hotkey.toggle.desc", "Activa o desactiva el modo fácil (vista previa de paradas de autobús)." },
                { "binding.map", "Paradas de autobús automáticas" },
                { "version.label", "Versión" },
                { "author.label", "Autor" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Hilo del foro" },
                { "link.rainbow", "Sitio RAINBOW" },
                { "btn.toggle", "Colocar paradas automáticamente" },
                { "warn.selectStation", "No hay ninguna parada de autobús disponible" },
                { "warn.unsupportedLine", "Este mod solo funciona con líneas de autobús." },
                { "warn.noPlace", "Aquí no cabe una parada" },
                { "warn.masterOff", "La colocación automática está desactivada en los ajustes" },
            };
        }

        private static Dictionary<string, string> Fr()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Arrêts de bus automatiques" },
                { "tab.general", "Général" },
                { "group.main", "Mode facile" },
                { "group.about", "À propos" },
                { "master.label", "Placement automatique des arrêts" },
                { "master.desc", "Le mode facile agit dans l'outil de ligne. Pendant que vous tracez une ligne de bus, immobilisez le curseur environ 0,2 s et le jeu affiche un aperçu de l'arrêt sur le trottoir ; c'est votre propre clic gauche qui le pose, et le point de passage s'y rattache. Rien n'est placé automatiquement et le mod n'appuie jamais sur les boutons à votre place ; un emplacement refusé par le jeu n'affiche aucun aperçu. Mode facile activé, le panneau des transports ne liste que les arrêts de bus." },
                { "policy.label", "À la sortie du mode facile" },
                { "policy.desc", "Ne concerne que les arrêts de bus posés automatiquement par ce mod pendant cette session ; un arrêt déjà utilisé par un point de passage d'une ligne est toujours conservé. Annuler une ligne inachevée est le comportement du jeu : il supprime ses points de passage et le mod ne s'en mêle pas." },
                { "tram.label", "Arrêts de tram (pas encore disponibles)" },
                { "tram.desc", "Les arrêts de tramway ne suivent pas les mêmes règles que les arrêts de bus (une ligne de tram se trace d'arrêt en arrêt). Cette partie n'est pas terminée : l'option reste ici, grisée, et une mise à jour future l'ouvrira." },
                { "warn.tramNotOnline", "Les arrêts de tram ne sont pas encore disponibles : le mode facile ne gère que les lignes de bus." },
                { "policy.keep", "Conserver les arrêts" },
                { "policy.delete", "Ne pas conserver les arrêts" },
                { "hotkey.toggle", "Basculer le mode facile" },
                { "hotkey.toggle.desc", "Active ou désactive le mode facile (aperçu des arrêts de bus)." },
                { "binding.map", "Arrêts de bus automatiques" },
                { "version.label", "Version" },
                { "author.label", "Auteur" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Sujet du forum" },
                { "link.rainbow", "Site RAINBOW" },
                { "btn.toggle", "Placer les arrêts automatiquement" },
                { "warn.selectStation", "Aucun arrêt de bus disponible" },
                { "warn.unsupportedLine", "Ce mod ne fonctionne qu'avec les lignes de bus." },
                { "warn.noPlace", "Pas de place pour un arrêt ici" },
                { "warn.masterOff", "Le placement automatique est désactivé dans les options" },
            };
        }

        private static Dictionary<string, string> It()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Fermate dell'autobus automatiche" },
                { "tab.general", "Generale" },
                { "group.main", "Modalità facile" },
                { "group.about", "Informazioni" },
                { "master.label", "Posizionamento automatico delle fermate" },
                { "master.desc", "La modalità facile agisce dentro lo strumento linea. Mentre tracci una linea autobus, tieni fermo il cursore per circa 0,2 s e il gioco mostra l'anteprima della fermata sul marciapiede; a posarla è il tuo clic sinistro, e la tappa della linea le si aggancia. Nulla viene piazzato automaticamente e il mod non preme pulsanti al posto tuo; un punto che il gioco rifiuta non mostra anteprima. Con la modalità facile attiva, il pannello trasporti elenca solo fermate dell'autobus." },
                { "policy.label", "All'uscita dalla modalità facile" },
                { "policy.desc", "Riguarda solo le fermate dell'autobus posate automaticamente da questo mod in questa sessione; una fermata già usata da una tappa di una linea resta sempre. Annullare una linea non completata è il comportamento del gioco: le tappe le scarta il gioco e il mod non interviene." },
                { "tram.label", "Fermate dei tram (non ancora disponibili)" },
                { "tram.desc", "Le fermate del tram seguono regole diverse da quelle degli autobus (una linea del tram si disegna da fermata a fermata). Questa parte non è ancora pronta: la voce resta qui, disattivata, e un aggiornamento futuro la sbloccherà." },
                { "warn.tramNotOnline", "Le fermate dei tram non sono ancora disponibili: la modalità facile gestisce solo le linee degli autobus." },
                { "policy.keep", "Mantieni le fermate" },
                { "policy.delete", "Non mantenere le fermate" },
                { "hotkey.toggle", "Attiva/disattiva modalità facile" },
                { "hotkey.toggle.desc", "Attiva o disattiva la modalità facile (anteprima fermate autobus)." },
                { "binding.map", "Fermate dell'autobus automatiche" },
                { "version.label", "Versione" },
                { "author.label", "Autore" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Discussione sul forum" },
                { "link.rainbow", "Sito RAINBOW" },
                { "btn.toggle", "Posiziona fermate automaticamente" },
                { "warn.selectStation", "Nessuna fermata dell'autobus disponibile" },
                { "warn.unsupportedLine", "Questo mod funziona solo con le linee degli autobus." },
                { "warn.noPlace", "Non c’è spazio per una fermata qui" },
                { "warn.masterOff", "Il posizionamento automatico è disattivato nelle opzioni" },
            };
        }

        private static Dictionary<string, string> Ja()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "バス停の自動設置" },
                { "tab.general", "一般" },
                { "group.main", "かんたんモード" },
                { "group.about", "このMODについて" },
                { "master.label", "停留所の自動設置" },
                { "master.desc", "かんたんモードは路線ツールの中で動きます。バス路線を引いている間にカーソルを約0.2秒止めると、ゲーム側が歩道にバス停のプレビューを出します。確定するのはご自身の左クリックで、通過地点はその停留所に接続されます。自動で設置されることはなく、MODがボタンの代押しをすることはありません。ゲームが拒否する場所にはプレビューも出ません。かんたんモード中は交通パネルにバス停のみが並びます。" },
                { "policy.label", "かんたんモードを終えるとき" },
                { "policy.desc", "対象は、このセッションで MOD が自動設置したバス停だけ。路線の通過地点がすでに使っている停留所は常に残します。未完成の路線をキャンセルするのは原版の挙動で、通過地点はゲーム側が消すため、この MOD は干渉しません。" },
                { "tram.label", "路面電車停留所の自動設置（未実装）" },
                { "tram.desc", "路面電車の停留所はバス停と異なる配置ルール（停留所から停留所へ路線を引く）のため、この部分はまだ未完成です。項目はグレーのまま予約し、今後のアップデートで開放します。" },
                { "warn.tramNotOnline", "路面電車停留所は未実装です。かんたんモードはバス路線のみ対応。" },
                { "policy.keep", "停留所を残す" },
                { "policy.delete", "停留所を残さない" },
                { "hotkey.toggle", "かんたんモード切替" },
                { "hotkey.toggle.desc", "かんたんモード（バス停プレビュー）のオン/オフを切り替えます。" },
                { "binding.map", "バス停の自動設置" },
                { "version.label", "バージョン" },
                { "author.label", "作者" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "フォーラム" },
                { "link.rainbow", "RAINBOW 公式サイト" },
                { "btn.toggle", "バス停／電車停留所を自動設置" },
                { "warn.selectStation", "利用可能なバス停がありません" },
                { "warn.unsupportedLine", "このMODはバス路線のみに対応しています。" },
                { "warn.noPlace", "ここに停留所を設置できません" },
                { "warn.masterOff", "設定で停留所の自動設置がオフになっています" },
            };
        }

        private static Dictionary<string, string> Ko()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "버스 정류장 자동 배치" },
                { "tab.general", "일반" },
                { "group.main", "간편 모드" },
                { "group.about", "정보" },
                { "master.label", "정류장 자동 배치" },
                { "master.desc", "간편 모드는 노선 도구 안에서 동작합니다. 버스 노선을 긋는 동안 커서를 약 0.2초 정지하면 게임이 인도의 버스 정류장 미리보기를 보여 줍니다. 실제로 두는 것은 사용자의 좌클릭이며, 웨이포인트가 그 정류장에 연결됩니다. 자동으로 배치되거나 모드가 대신 버튼을 누르지는 않고, 게임이 거부한 위치에는 미리보기가 나타나지 않습니다. 간편 모드 중에는 교통 패널에 버스 정류장만 표시됩니다." },
                { "policy.label", "간편 모드를 끝낼 때" },
                { "policy.desc", "이번 세션에서 모드가 자동 배치한 버스 정류장만 대상입니다. 노선의 웨이포인트가 이미 사용 중인 정류장은 항상 유지됩니다. 노선을 완성하지 않고 취소하는 것은 원본 동작이며, 웨이포인트는 게임이 스스로 지우므로 이 모드가 관여하지 않습니다." },
                { "tram.label", "전차 정류장 자동 배치(미출시)" },
                { "tram.desc", "전차 정류장은 버스 정류장과 배치 규칙이 다릅니다(전차 노선은 정류장에서 정류장으로만 그릴 수 있음). 이 부분은 아직 완성되지 않았으니 항목은 비활성으로 예약하고 추후 업데이트에서 개방합니다." },
                { "warn.tramNotOnline", "전차 정류장은 아직 출시되지 않았습니다. 간편 모드는 버스 노선만 처리합니다." },
                { "policy.keep", "정류장 유지" },
                { "policy.delete", "정류장 유지하지 않음" },
                { "hotkey.toggle", "간편 모드 전환" },
                { "hotkey.toggle.desc", "간편 모드(버스 정류장 미리보기)를 켜거나 끕니다." },
                { "binding.map", "버스 정류장 자동 배치" },
                { "version.label", "버전" },
                { "author.label", "작성자" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "포럼 스레드" },
                { "link.rainbow", "RAINBOW 공식 사이트" },
                { "btn.toggle", "정류장 자동 배치" },
                { "warn.selectStation", "사용 가능한 버스 정류장이 없습니다" },
                { "warn.unsupportedLine", "이 모드는 버스 노선에서만 동작합니다." },
                { "warn.noPlace", "여기에 정류장을 설치할 공간이 없습니다" },
                { "warn.masterOff", "설정에서 정류장 자동 배치가 꺼져 있습니다" },
            };
        }

        private static Dictionary<string, string> Pl()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Automatyczne przystanki autobusowe" },
                { "tab.general", "Ogólne" },
                { "group.main", "Tryb łatwy" },
                { "group.about", "Informacje" },
                { "master.label", "Automatyczne stawianie przystanków" },
                { "master.desc", "Tryb łatwy działa wewnątrz narzędzia linii. Gdy rysujesz linię autobusową, przytrzymaj kursor w miejscu około 0,2 s, a gra pokaże podgląd przystanku na chodniku; stawia go dopiero Twoje własne lewe kliknięcie, a węzeł trasy przypina się do tego przystanku. Nic nie jest stawiane automatycznie i mod nigdy nie klika za Ciebie; miejsce odrzucone przez grę nie pokazuje podglądu. Przy włączonym trybie łatwym panel transportu pokazuje wyłącznie przystanki autobusowe." },
                { "policy.label", "Przy wyjściu z trybu łatwego" },
                { "policy.desc", "Dotyczy wyłącznie przystanków autobusowych postawionych automatycznie przez ten mod w tej sesji; przystanek, którego używa już węzeł trasy, jest zawsze zachowywany. Anulowanie nieukończonej trasy to zachowanie gry: węzły trasy usuwa gra, a mod w to nie wchodzi." },
                { "tram.label", "Przystanki tramwajowe (jeszcze niedostępne)" },
                { "tram.desc", "Przystanki tramwajowe mają inne zasady stawiania niż autobusowe (linię tramwajową rysuje się od przystanku do przystanku). Ta część nie jest jeszcze gotowa — opcja pozostaje tu, szara, i odblokuje ją późniejsza aktualizacja." },
                { "warn.tramNotOnline", "Przystanki tramwajowe nie są jeszcze dostępne — tryb łatwy działa tylko z liniami autobusowymi." },
                { "policy.keep", "Zostaw przystanki" },
                { "policy.delete", "Nie zostawiaj przystanków" },
                { "hotkey.toggle", "Przełącz tryb łatwy" },
                { "hotkey.toggle.desc", "Włącza lub wyłącza tryb łatwy (podgląd przystanków autobusowych)." },
                { "binding.map", "Automatyczne przystanki" },
                { "version.label", "Wersja" },
                { "author.label", "Autor" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Wątek na forum" },
                { "link.rainbow", "Strona RAINBOW" },
                { "btn.toggle", "Automatyczne stawianie przystanków" },
                { "warn.selectStation", "Brak dostępnego przystanku autobusowego" },
                { "warn.unsupportedLine", "Ten mod działa tylko z liniami autobusowymi." },
                { "warn.noPlace", "Tu nie ma miejsca na przystanek" },
                { "warn.masterOff", "Automatyczne stawianie przystanków jest wyłączone w ustawieniach" },
            };
        }

        private static Dictionary<string, string> Pt()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Paradas de ônibus automáticas" },
                { "tab.general", "Geral" },
                { "group.main", "Modo fácil" },
                { "group.about", "Sobre" },
                { "master.label", "Colocação automática de pontos" },
                { "master.desc", "O modo fácil age dentro da ferramenta de linha. Enquanto desenha uma linha de ônibus, mantenha o cursor parado por cerca de 0,2 s e o jogo mostra a pré-visualização do ponto na calçada; quem o coloca é o seu clique esquerdo, e o ponto de passagem se prende a esse ponto. Nada é colocado automaticamente e o mod nunca clica botões por você; um local que o jogo recusa não mostra pré-visualização. Com o modo fácil ativo, o painel de transporte lista apenas pontos de ônibus." },
                { "policy.label", "Ao sair do modo fácil" },
                { "policy.desc", "Só afeta os pontos de ônibus colocados automaticamente por este mod nesta sessão; um ponto já usado por um ponto de passagem de uma linha é sempre mantido. Cancelar uma linha não concluída é comportamento do jogo: ele descarta os pontos de passagem e este mod não interfere." },
                { "tram.label", "Pontos de bonde (ainda indisponíveis)" },
                { "tram.desc", "Pontos de bonde têm regras de posicionamento diferentes dos pontos de ônibus (a linha de bonde é traçada de ponto em ponto). Esta parte ainda não está pronta; a opção fica aqui bloqueada e uma atualização futura a liberará." },
                { "warn.tramNotOnline", "Pontos de bonde ainda não estão disponíveis: o modo fácil só funciona com linhas de ônibus." },
                { "policy.keep", "Manter os pontos" },
                { "policy.delete", "Não manter os pontos" },
                { "hotkey.toggle", "Alternar modo fácil" },
                { "hotkey.toggle.desc", "Ativa ou desativa o modo fácil (pré-visualização de pontos de ônibus)." },
                { "binding.map", "Paradas de ônibus automáticas" },
                { "version.label", "Versão" },
                { "author.label", "Autor" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Tópico do fórum" },
                { "link.rainbow", "Site RAINBOW" },
                { "btn.toggle", "Colocar pontos automaticamente" },
                { "warn.selectStation", "Nenhum ponto de ônibus disponível" },
                { "warn.unsupportedLine", "Este mod funciona só com linhas de ônibus." },
                { "warn.noPlace", "Não há espaço para um ponto aqui" },
                { "warn.masterOff", "A colocação automática está desligada nas configurações" },
            };
        }

        private static Dictionary<string, string> Ru()
        {
            return new Dictionary<string, string>
            {
                { "mod.name", "Автоматические автобусные остановки" },
                { "tab.general", "Основные" },
                { "group.main", "Простой режим" },
                { "group.about", "О моде" },
                { "master.label", "Автоматическая установка остановок" },
                { "master.desc", "Простой режим работает внутри инструмента линии. Когда вы ведёте автобусную линию и держите курсор неподвижно около 0,2 с, игра показывает предпросмотр остановки на тротуаре; ставит её именно ваш левый клик, а путевая точка привязывается к этой остановке. Ничего не размещается автоматически, и мод никогда не нажимает кнопки за вас; там, где игра отказывает, предпросмотр не появляется. Пока простой режим включён, панель транспорта показывает только автобусные остановки." },
                { "policy.label", "При выходе из простого режима" },
                { "policy.desc", "Касается только остановок, которые мод автоматически поставил в этой сессии; остановку, которую уже использует точка маршрута, мод никогда не удаляет. Отмена незавершенного маршрута — это поведение игры: точки маршрута удаляет сама игра, и мод в это не вмешивается." },
                { "tram.label", "Трамвайные остановки (ещё недоступны)" },
                { "tram.desc", "У трамвайных остановок другие правила размещения, чем у автобусных (линию трамвая проводят от остановки к остановке). Эта часть ещё не готова — пункт остаётся здесь неактивным, его откроет будущее обновление." },
                { "warn.tramNotOnline", "Трамвайные остановки ещё недоступны — простой режим работает только с автобусными линиями." },
                { "policy.keep", "Сохранить остановки" },
                { "policy.delete", "Не сохранять остановки" },
                { "hotkey.toggle", "Переключить простой режим" },
                { "hotkey.toggle.desc", "Включает или выключает простой режим (предпросмотр автобусных остановок)." },
                { "binding.map", "Автоматические остановки" },
                { "version.label", "Версия" },
                { "author.label", "Автор" },
                { "link.kofi", "Ko-fi" },
                { "link.forum", "Тема на форуме" },
                { "link.rainbow", "Сайт RAINBOW" },
                { "btn.toggle", "Авторазмещение остановок" },
                { "warn.selectStation", "Нет доступных автобусных остановок" },
                { "warn.unsupportedLine", "Этот мод работает только с автобусными линиями." },
                { "warn.noPlace", "Здесь нет места для остановки" },
                { "warn.masterOff", "Автоматическая установка отключена в настройках" },
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

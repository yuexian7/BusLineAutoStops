/**
 * patch-locale-060.js — 把 LocaleTable.cs 的 12 份词条改成 v0.6.0 的语义
 * · master.desc / policy.label / policy.desc 全部重写（懒人模式不再占用线路工具）
 * · policy.nothing/all/stations/stops 四行 → policy.keep / policy.delete 两行（需求 5）
 * · 新增 warn.applyUnavailable（代按放置键不可用时的提示）
 * · BuildPanelMap 注册新 slug；表头注释的键数改 24
 * 幂等：已经改过的语言会跳过（检测 policy.keep 是否已存在）。
 */
const fs = require("fs");
const path = require("path");

const FILE = path.join(__dirname, "..", "LocaleTable.cs");
let src = fs.readFileSync(FILE, "utf8");

const T = {
  "en-US": {
    master: "Automatic placement for bus stops and tram stops. Press the mod icon at the top left or the hotkey at any moment and lazy mode turns on: the transport panel then lists only stop assets of that kind. Once you pick one, the game's own placement works as usual - the preview follows your cursor and snaps to the sidewalk or the median. If the cursor rests on a valid spot for 0.1 s, the mod presses the place button for you, so stops appear one after another as you walk along the road. A spot the game would refuse is never clicked, so no bogus error messages. Pressing the icon or hotkey again only stops these automatic clicks - your tools, lines and existing stops are left alone.",
    label: "When lazy mode ends",
    desc: "Only covers bus stops this mod placed automatically during this session; a stop that a line's waypoints already use is always kept. Cancelling an unfinished line is vanilla behaviour - the game drops those waypoints itself and this mod does not get involved.",
    keep: "Keep the bus stops",
    del: "Remove the bus stops",
    warn: "This game build does not let the mod press the place button, so lazy mode only filters the asset panel.",
  },
  "zh-HANS": {
    master: "自动放置公交站／有轨电车站。任何时候点左上角的模组图标或按快捷键都能开启懒人模式：此时交通面板只列出同类的站台资产。选中一个之后，原版的放置流程照旧 —— 预览跟着光标走，并吸附到人行道或分界线上。光标在合法的位置停住 0.1 秒，模组就替你按下放置键，沿路走一路放，公交站一个一个自己排好。原版不给放的位置一律不点，所以不会多出假的报错。再按一次图标或快捷键只是停止这些自动代点，你的工具、线路和已经放好的站台都不受影响。",
    label: "退出懒人模式时",
    desc: "只管本次会话里由模组自动落地的公交站；已经被线路路径点用上的站台永远保留。线路没画完就取消是原版行为 —— 路径点由游戏自己丢弃，本模组不参与、也不对抗。",
    keep: "保留公交站",
    del: "不保留公交站",
    warn: "当前的游戏版本不让模组代按放置键，这次懒人模式只过滤资产面板。",
  },
  "zh-HANT": {
    master: "自動放置公車站／電車站。任何時候點左上角的模組圖示或按快捷鍵都能開啟懶人模式：此時交通面板只列出同類的站點資產。選取一個之後，原版的放置流程照舊 —— 預覽跟著游標走，並吸附到人行道或分隔線上。游標在合法的位置停住 0.1 秒，模組就替你按下放置鍵，沿路走一路放，公車站一個一個自己排好。原版不給放的位置一律不點，所以不會多出假的錯誤提示。再按一次圖示或快捷鍵只是停止這些自動代點，你的工具、路線與已放好的站點都不受影響。",
    label: "退出懶人模式時",
    desc: "只處理本次會話裡由模組自動落地的公車站；已被路線路徑點使用的站點永遠保留。路線沒畫完就取消是原版行為 —— 路徑點由遊戲自行捨棄，本模組不干預、也不對抗。",
    keep: "保留公車站",
    del: "不保留公車站",
    warn: "目前的遊戲版本不允許模組代按放置鍵，這次懶人模式只過濾資產面板。",
  },
  "de-DE": {
    master: "Setzt Bushaltestellen und Straßenbahnhaltestellen automatisch. Das Symbol links oben oder das Tastenkürzel starten den Komfortmodus jederzeit: Im Verkehrspanel erscheinen dann nur Haltestellen dieser Art. Nach der Auswahl setzt das Spiel wie gewohnt – die Vorschau folgt dem Cursor und rastet am Gehweg oder Mittelstreifen ein. Bleibt der Cursor 0,1 s an einer zulässigen Stelle, drückt der Mod die Setzen-Taste für dich, also entstehen beim Entlangfahren eine Haltestelle nach der anderen. Eine abgelehnte Stelle wird nie geklickt, daher gibt es keine falschen Fehlermeldungen. Erneutes Drücken stoppt nur diese automatischen Klicks – Werkzeuge, Linien und vorhandene Haltestellen bleiben unberührt.",
    label: "Beim Beenden des Komfortmodus",
    desc: "Betrifft nur Bushaltestellen, die dieser Mod in dieser Sitzung automatisch gesetzt hat; eine Haltestelle, die Wegpunkte einer Linie schon nutzen, bleibt immer erhalten. Eine unvollständige Linie abzubrechen ist Originalverhalten – das Spiel verwirft die Wegpunkte selbst, der Mod mischt sich nicht ein.",
    keep: "Haltestellen behalten",
    del: "Haltestellen entfernen",
    warn: "Dieses Spielbuild erlaubt dem Mod das Drücken der Setzen-Taste nicht, daher filtert der Komfortmodus nur die Objektliste.",
  },
  "es-ES": {
    master: "Coloca automáticamente paradas de autobús y de tranvía. Pulsa el icono de arriba a la izquierda o el atajo en cualquier momento para entrar en el modo fácil: el panel de transporte mostrará entonces solo paradas de ese tipo. Al elegir una, el juego coloca como siempre: la previsualización sigue el cursor y se ajusta a la acera o a la mediana. Si el cursor se queda 0,1 s en un punto válido, el mod pulsa el botón de colocar por ti, así que las paradas aparecen mientras recorres la calzada. Nunca pulsa un punto que el juego rechazaría, así que no verás errores falsos. Volver a pulsar solo detiene estos clics automáticos: tus herramientas, líneas y paradas existentes quedan igual.",
    label: "Al salir del modo fácil",
    desc: "Solo afecta a las paradas de autobús que este mod colocó automáticamente en esta sesión; una parada que los puntos de una línea ya usan se conserva siempre. Cancelar una línea sin terminar es comportamiento original: el juego descarta sus puntos y este mod no interviene.",
    keep: "Conservar las paradas",
    del: "No conservar las paradas",
    warn: "Esta versión del juego no permite que el mod pulse el botón de colocar, así que el modo fácil solo filtra el panel de objetos.",
  },
  "fr-FR": {
    master: "Place automatiquement les arrêts de bus et de tramway. Appuyez sur l'icône en haut à gauche ou sur le raccourci, à tout moment : le mode facile s'active et le panneau des transports ne liste plus que les arrêts de ce type. Une fois l'un sélectionné, la pose du jeu fonctionne comme d'habitude — l'aperçu suit le curseur et se cale sur le trottoir ou le terre-plein central. Quand le curseur reste 0,1 s sur un emplacement valide, le mod appuie sur le bouton de pose à votre place ; les arrêts s'alignent le long de la chaussée. Un emplacement que le jeu refuse n'est jamais cliqué, donc aucune fausse erreur. Une nouvelle pression n'arrête que ces clics automatiques : vos outils, lignes et arrêts existants restent en place.",
    label: "À la sortie du mode facile",
    desc: "Ne concerne que les arrêts de bus posés automatiquement par ce mod pendant cette session ; un arrêt déjà utilisé par un point de passage d'une ligne est toujours conservé. Annuler une ligne inachevée est le comportement du jeu : il supprime ses points de passage et le mod ne s'en mêle pas.",
    keep: "Conserver les arrêts",
    del: "Ne pas conserver les arrêts",
    warn: "Cette version du jeu n'autorise pas le mod à appuyer sur le bouton de pose ; le mode facile ne filtre donc que le panneau d'objets.",
  },
  "it-IT": {
    master: "Posa automaticamente fermate di autobus e di tram. Premi l'icona in alto a sinistra o la scorciatoia in qualunque momento: la modalità facile si attiva e il pannello dei trasporti elenca solo fermate di quel tipo. Dopo averne scelta una, la posa del gioco funziona come sempre: l'anteprima segue il cursore e si aggancia al marciapiede o allo spartitraffico. Se il cursore resta 0,1 s su un punto valido, il mod preme il pulsante di posa al posto tuo, così le fermate compaiono una dopo l'altra lungo la strada. Un punto che il gioco rifiuterebbe non viene mai cliccato, quindi niente errori fantasma. Premendo di nuovo si fermano solo i clic automatici: strumenti, linee e fermate esistenti restano invariati.",
    label: "All'uscita dalla modalità facile",
    desc: "Riguarda solo le fermate dell'autobus posate automaticamente da questo mod in questa sessione; una fermata già usata da una tappa di una linea resta sempre. Annullare una linea non completata è il comportamento del gioco: le tappe le scarta il gioco e il mod non interviene.",
    keep: "Mantieni le fermate",
    del: "Non mantenere le fermate",
    warn: "Questa versione del gioco non consente al mod di premere il pulsante di posa, quindi la modalità facile filtra solo il pannello degli oggetti.",
  },
  "ja-JP": {
    master: "バス停と路面電車停留所を自動で設置します。左上のアイコンかホットキーはどこで押してもかんたんモードになり、その種の停留所アセットだけが並びます。選ぶと原版どおりに設置できます：プレビューがカーソルについて歩き、歩道か中央分離帯に吸い寄せられます。設置できる場所でカーソルを 0.1 秒止めると、MOD が代わりに設置ボタンを押すので、道路沿いを進むたびに停留所が増えていきます。原版が断る場所では絶対に押さないので、見かけ上のエラーも増えません。もう一度押すと自動の代押しだけが止まり、ツール・路線・すでに置いた停留所はそのままです。",
    label: "かんたんモードを終えるとき",
    desc: "対象は、このセッションで MOD が自動設置したバス停だけ。路線の通過地点がすでに使っている停留所は常に残します。未完成の路線をキャンセルするのは原版の挙動で、通過地点はゲーム側が消すため、この MOD は干渉しません。",
    keep: "停留所を残す",
    del: "停留所を残さない",
    warn: "このゲーム版では MOD が設置ボタンを代押しできないため、かんたんモードはアセット一覧の絞り込みだけ行います。",
  },
  "ko-KR": {
    master: "버스 정류장과 트램 정류장을 자동으로 배치합니다. 왼쪽 위 아이콘이나 단축키는 언제 눌러도 간편 모드가 켜지고, 그때 교통 패널에는 같은 종류 정류장 애셋만 표시됩니다. 하나를 고르면 원본 배치가 그대로 동작합니다 — 미리보기가 마우스를 따라가며 인도나 가운데 분리대에 붙습니다. 배치 가능한 자리에서 마우스를 0.1초 멈추면 모드가 대신 배치 버튼을 누르므로, 도로를 따라 이동하는 동안 정류장이 계속 놓입니다. 원본이 거부하는 자리에서는 절대 누르지 않으니 엉뚱한 오류도 뜨지 않습니다. 한 번 더 누르면 자동 누름만 멈추고, 도구·노선·이미 놓인 정류장은 그대로 남습니다.",
    label: "간편 모드를 끝낼 때",
    desc: "이번 세션에서 모드가 자동 배치한 버스 정류장만 대상입니다. 노선의 웨이포인트가 이미 사용 중인 정류장은 항상 유지됩니다. 노선을 완성하지 않고 취소하는 것은 원본 동작이며, 웨이포인트는 게임이 스스로 지우므로 이 모드가 관여하지 않습니다.",
    keep: "정류장 유지",
    del: "정류장 유지하지 않음",
    warn: "현재 게임 버전에서는 모드가 배치 버튼을 대신 누를 수 없어, 간편 모드는 애셋 목록 필터만 적용됩니다.",
  },
  "pl-PL": {
    master: "Automatycznie stawia przystanki autobusowe i tramwajowe. Ikona u góry po lewej albo skrót włączają tryb łatwy w każdej chwili: panel transportu pokazuje wtedy tylko przystanki tego rodzaju. Po wybraniu któregoś gra stawia je jak zwykle — podgląd podąża za kursorem i przylega do chodnika lub pasa rozdziału. Jeśli kursor zostanie 0,1 s w dozwolonym miejscu, mod naciska przycisk stawiania za Ciebie, więc przystanki pojawiają się jeden po drugim wzdłuż jezdni. Miejsc, których gra by nie zaakceptowała, nigdy nie klika, więc nie zobaczysz fałszywych błędów. Ponowne naciśnięcie zatrzymuje tylko te automatyczne kliknięcia — narzędzia, trasy i już postawione przystanki zostają bez zmian.",
    label: "Przy wyjściu z trybu łatwego",
    desc: "Dotyczy wyłącznie przystanków autobusowych postawionych automatycznie przez ten mod w tej sesji; przystanek, którego używa już węzeł trasy, jest zawsze zachowywany. Anulowanie nieukończonej trasy to zachowanie gry: węzły trasy usuwa gra, a mod w to nie wchodzi.",
    keep: "Zostaw przystanki",
    del: "Nie zostawiaj przystanków",
    warn: "Ta wersja gry nie pozwala modowi naciskać przycisku stawiania, więc tryb łatwy tylko filtruje listę obiektów.",
  },
  "pt-BR": {
    master: "Coloca automaticamente pontos de ônibus e de bonde. Clique no ícone no canto superior esquerdo ou no atalho a qualquer momento para entrar no modo fácil: o painel de transporte passa a listar só pontos desse tipo. Depois de escolher um, a colocação do jogo funciona normalmente — a pré-visualização segue o cursor e se encaixa na calçada ou no canteiro central. Se o cursor ficar 0,1 s num local válido, o mod aperta o botão de colocar por você, então os pontos aparecem um após outro enquanto você percorre a via. Local que o jogo recusaria nunca é clicado, portanto nenhum erro falso aparece. Clicar de novo só para esses cliques automáticos: suas ferramentas, linhas e pontos já colocados ficam como estão.",
    label: "Ao sair do modo fácil",
    desc: "Só afeta os pontos de ônibus colocados automaticamente por este mod nesta sessão; um ponto já usado por um ponto de passagem de uma linha é sempre mantido. Cancelar uma linha não concluída é comportamento do jogo: ele descarta os pontos de passagem e este mod não interfere.",
    keep: "Manter os pontos",
    del: "Não manter os pontos",
    warn: "Esta versão do jogo não permite que o mod aperte o botão de colocar, então o modo fácil apenas filtra o painel de objetos.",
  },
  "ru-RU": {
    master: "Автоматически расставляет остановки автобусов и трамваев. Нажмите значок в левом верхнем углу или горячую клавишу в любой момент — простой режим включится, и в панели транспорта останутся только остановки этого вида. После выбора игра расставляет их как обычно: призрак следует за курсором и прилипает к тротуару или разделительной полосе. Если курсор задержится 0,1 с в подходящем месте, мод нажмет кнопку размещения вместо вас, и остановки появятся одна за другой вдоль дороги. Неудачные места не кликаются вовсе, так что ложных ошибок не будет. Повторное нажатие лишь останавливает эти автоматические клики — инструменты, маршруты и уже поставленные остановки остаются нетронутыми.",
    label: "При выходе из простого режима",
    desc: "Касается только остановок, которые мод автоматически поставил в этой сессии; остановку, которую уже использует точка маршрута, мод никогда не удаляет. Отмена незавершенного маршрута — это поведение игры: точки маршрута удаляет сама игра, и мод в это не вмешивается.",
    keep: "Сохранить остановки",
    del: "Не сохранять остановки",
    warn: "В этой версии игры мод не может нажать кнопку размещения, поэтому простой режим только фильтрует панель объектов.",
  },
};

const esc = (s) => s;

let changed = 0, skipped = 0, problems = [];

/** 取某个 locale 的方法体（从 `case "xx"` 对应的 En()/ZhHans() ... 开始） */
const FN = {
  "en-US": "En", "zh-HANS": "ZhHans", "zh-HANT": "ZhHant", "ja-JP": "Ja", "ko-KR": "Ko",
  "fr-FR": "Fr", "de-DE": "De", "es-ES": "Es", "it-IT": "It", "pl-PL": "Pl",
  "pt-BR": "Pt", "ru-RU": "Ru",
};

function blockRange(name) {
  const start = src.indexOf("private static Dictionary<string, string> " + name + "()");
  if (start < 0) return null;
  const end = src.indexOf("\n        }", src.indexOf("return new Dictionary<string, string>", start));
  if (end < 0) return null;
  return { start, end: end + 9 };
}

function setKey(body, key, value) {
  const re = new RegExp('(\\{ "' + key + '", ")[^"]*(" \\})');
  if (!re.test(body)) { problems.push(key + " 没找到"); return body; }
  return body.replace(re, "$1" + value.replace(/\$/g, "$$$$") + "$2");
}

for (const loc of Object.keys(FN)) {
  const r = blockRange(FN[loc]);
  if (!r) { problems.push(loc + "：方法体没找到"); continue; }
  let body = src.slice(r.start, r.end);
  if (body.includes('"policy.keep"')) { skipped++; continue; }
  const t = T[loc];

  body = setKey(body, "master.desc", t.master);
  body = setKey(body, "policy.label", t.label);
  body = setKey(body, "policy.desc", t.desc);

  // 四行 → 两行
  const four = /( *)\{ "policy\.nothing", "[^"]*" \},\r?\n * \{ "policy\.all", "[^"]*" \},\r?\n * \{ "policy\.stations", "[^"]*" \},\r?\n * \{ "policy\.stops", "[^"]*" \},/;
  if (!four.test(body)) { problems.push(loc + "：policy 四行没匹配"); continue; }
  body = body.replace(four, (_m, ind) =>
    ind + '{ "policy.keep", "' + t.keep + '" },\n' + ind + '{ "policy.delete", "' + t.del + '" },');

  // warn.applyUnavailable 紧跟 warn.masterOff
  const wm = /( *)\{ "warn\.masterOff", "[^"]*" \},/;
  if (!wm.test(body)) { problems.push(loc + "：warn.masterOff 没找到"); continue; }
  if (!body.includes('"warn.applyUnavailable"')) {
    body = body.replace(wm, (_m, ind) => _m + "\n" + ind + '{ "warn.applyUnavailable", "' + t.warn + '" },');
  }

  src = src.slice(0, r.start) + body + src.slice(r.end);
  changed++;
}

// BuildPanelMap 注册新 slug
if (!src.includes("WARN_APPLY_UNAVAILABLE")) {
  src = src.replace(
    'string[] slugs = { "BTN_TOGGLE", "WARN_SELECT_STATION", "WARN_UNSUPPORTED_LINE", "WARN_NO_PLACE", "WARN_MASTER_OFF" };',
    'string[] slugs = { "BTN_TOGGLE", "WARN_SELECT_STATION", "WARN_UNSUPPORTED_LINE", "WARN_NO_PLACE", "WARN_MASTER_OFF", "WARN_APPLY_UNAVAILABLE" };');
  src = src.replace(
    'string[] keys = { "btn.toggle", "warn.selectStation", "warn.unsupportedLine", "warn.noPlace", "warn.masterOff" };',
    'string[] keys = { "btn.toggle", "warn.selectStation", "warn.unsupportedLine", "warn.noPlace", "warn.masterOff", "warn.applyUnavailable" };');
}

// 表头注释里的键数：删 4 加 2 加 1 → 24 键
src = src.replace("所有语言键集一致（25 键）", "所有语言键集一致（24 键）");

fs.writeFileSync(FILE, src, "utf8");
console.log("改写 " + changed + " 种，跳过 " + skipped + " 种");

// 复核
const after = fs.readFileSync(FILE, "utf8");
const must = ["policy.keep", "policy.delete", "warn.applyUnavailable"];
// 大写的 WARN_APPLY_UNAVAILABLE 是面板 slug，只在 BuildPanelMap 里注册一次，不在词典体内
if (!after.includes("WARN_APPLY_UNAVAILABLE")) problems.push("BuildPanelMap 没注册 WARN_APPLY_UNAVAILABLE");
for (const loc of Object.values(FN)) {
  const r = blockRange.call(null, loc);
  const body = after.slice(after.indexOf("private static Dictionary<string, string> " + loc + "()"),
    after.indexOf("\n        }", after.indexOf("return new Dictionary<string, string>", after.indexOf("private static Dictionary<string, string> " + loc + "()"))));
  const miss = must.filter((k) => !body.includes('"' + k + '"'));
  const gone = ["policy.nothing", "policy.all", "policy.stations", "policy.stops"].filter((k) => body.includes('"' + k + '"'));
  if (miss.length || gone.length) {
    problems.push(loc + " 缺 " + (miss.join("/") || "-") + " 还剩 " + (gone.join("/") || "-"));
  }
  // 键数一致
  const n = (body.match(/\{ "/g) || []).length;
  if (n !== 24) problems.push(loc + " 键数 = " + n + "（应为 24）");
}
console.log(problems.length ? "问题：" + problems.join("；") : "12 语言 × 24 键齐，旧 4 项 policy 已清");
process.exit(problems.length ? 1 : 0);

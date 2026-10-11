// 0.10.0 需求 5 + 需求 3 的文案校正：
// master.desc / hotkey.toggle.desc / warn.selectStation / warn.unsupportedLine 这四条还写着
// "模组替你按放置键""0.1 秒""公交与有轨电车" —— 前一条在 v0.7.0 就撤掉了，
// 第二条本版改成约 0.2 秒，第三条本版电车暂未上线。按语言逐个替换值，不动键序。
const fs = require("fs");
const P = "D:/WorkSpace/CS2MOD/BusLineAutoStops/LocaleTable.cs";

const T = {
  En: {
    "master.desc": "Lazy mode works inside the line tool. While drawing a bus line, hold the cursor still for about 0.2 s and the game shows a preview bus stop on the pavement; your own left click is what places it, and the waypoint attaches to that stop. Nothing is ever placed automatically and the mod never presses buttons for you; a spot the game refuses shows no preview. With lazy mode on, the transport panel lists bus stop assets only.",
    "hotkey.toggle.desc": "Turns lazy mode (bus stop preview) on or off.",
    "warn.selectStation": "No bus stop asset available",
    "warn.unsupportedLine": "Only bus lines work with this mod.",
  },
  ZhHans: {
    "master.desc": "懒人模式挂在创建线路工具里。画公交线路时把光标停住约 0.2 秒，原版会在人行道上给出公交站的预览；真正放下的是你自己的左键，路径点会同时依附到那枚公交站。整个过程没有任何自动放置，模组也不会替你按键；原版不给放的位置不出现预览。懒人模式开着时，交通面板只列公交站资产。",
    "hotkey.toggle.desc": "开启或关闭懒人模式（公交站预览）。",
    "warn.selectStation": "没有可用的公交站资产",
    "warn.unsupportedLine": "本模组只处理公交线路。",
  },
  ZhHant: {
    "master.desc": "懶人模式掛在建立路線工具裡。畫公交路線時把光標停住約 0.2 秒，原版會在人行道上給出公車站預覽；真正放下的是你自己的左鍵，路徑點同時依附上下那個站。整個過程沒有自動放置，模組也不會替你按鍵；原版不給放的位置不會出現預覽。懶人模式開啟時，交通面板只列公車站資產。",
    "hotkey.toggle.desc": "開啟或關閉懶人模式（公車站預覽）。",
    "warn.selectStation": "沒有可用的公車站資產",
    "warn.unsupportedLine": "本模組只處理公交路線。",
  },
  De: {
    "master.desc": "Der Komfortmodus wirkt im Linienwerkzeug. Wenn du eine Buslinie zeichnest und den Cursor etwa 0,2 s ruhig hältst, zeigt das Spiel eine Vorschau der Bushaltestelle am Gehweg; gesetzt wird sie erst durch deinen eigenen Linksklick, und der Wegpunkt hängt sich an diese Haltestelle. Nichts wird automatisch platziert und der Mod drückt niemals Buttons für dich; eine Stelle, die das Spiel ablehnt, zeigt keine Vorschau. Solange der Komfortmodus aktiv ist, listet das Transportpanel nur Bushaltestellen.",
    "hotkey.toggle.desc": "Schaltet den Komfortmodus (Haltestellen-Vorschau) ein oder aus.",
    "warn.selectStation": "Keine Bushaltestelle verfügbar",
    "warn.unsupportedLine": "Dieser Mod funktioniert nur mit Buslinien.",
  },
  Es: {
    "master.desc": "El modo fácil actúa dentro de la herramienta de línea. Mientras trazas una línea de autobús, mantén el cursor quieto unos 0,2 s y el juego muestra una vista previa de la parada en la acera; lo que la coloca es tu propio clic izquierdo, y el punto de la ruta se une a esa parada. Nada se coloca automáticamente y el mod nunca pulsa botones por ti; una posición que el juego rechaza no muestra vista previa. Con el modo fácil activo, el panel de transporte solo lista paradas de autobús.",
    "hotkey.toggle.desc": "Activa o desactiva el modo fácil (vista previa de paradas de autobús).",
    "warn.selectStation": "No hay ninguna parada de autobús disponible",
    "warn.unsupportedLine": "Este mod solo funciona con líneas de autobús.",
  },
  Fr: {
    "master.desc": "Le mode facile agit dans l'outil de ligne. Pendant que vous tracez une ligne de bus, immobilisez le curseur environ 0,2 s et le jeu affiche un aperçu de l'arrêt sur le trottoir ; c'est votre propre clic gauche qui le pose, et le point de passage s'y rattache. Rien n'est placé automatiquement et le mod n'appuie jamais sur les boutons à votre place ; un emplacement refusé par le jeu n'affiche aucun aperçu. Mode facile activé, le panneau des transports ne liste que les arrêts de bus.",
    "hotkey.toggle.desc": "Active ou désactive le mode facile (aperçu des arrêts de bus).",
    "warn.selectStation": "Aucun arrêt de bus disponible",
    "warn.unsupportedLine": "Ce mod ne fonctionne qu'avec les lignes de bus.",
  },
  It: {
    "master.desc": "La modalità facile agisce dentro lo strumento linea. Mentre tracci una linea autobus, tieni fermo il cursore per circa 0,2 s e il gioco mostra l'anteprima della fermata sul marciapiede; a posarla è il tuo clic sinistro, e la tappa della linea le si aggancia. Nulla viene piazzato automaticamente e il mod non preme pulsanti al posto tuo; un punto che il gioco rifiuta non mostra anteprima. Con la modalità facile attiva, il pannello trasporti elenca solo fermate dell'autobus.",
    "hotkey.toggle.desc": "Attiva o disattiva la modalità facile (anteprima fermate autobus).",
    "warn.selectStation": "Nessuna fermata dell'autobus disponibile",
    "warn.unsupportedLine": "Questo mod funziona solo con le linee degli autobus.",
  },
  Ja: {
    "master.desc": "かんたんモードは路線ツールの中で動きます。バス路線を引いている間にカーソルを約0.2秒止めると、ゲーム側が歩道にバス停のプレビューを出します。確定するのはご自身の左クリックで、通過地点はその停留所に接続されます。自動で設置されることはなく、MODがボタンの代押しをすることはありません。ゲームが拒否する場所にはプレビューも出ません。かんたんモード中は交通パネルにバス停のみが並びます。",
    "hotkey.toggle.desc": "かんたんモード（バス停プレビュー）のオン/オフを切り替えます。",
    "warn.selectStation": "利用可能なバス停がありません",
    "warn.unsupportedLine": "このMODはバス路線のみに対応しています。",
  },
  Ko: {
    "master.desc": "간편 모드는 노선 도구 안에서 동작합니다. 버스 노선을 긋는 동안 커서를 약 0.2초 정지하면 게임이 인도의 버스 정류장 미리보기를 보여 줍니다. 실제로 두는 것은 사용자의 좌클릭이며, 웨이포인트가 그 정류장에 연결됩니다. 자동으로 배치되거나 모드가 대신 버튼을 누르지는 않고, 게임이 거부한 위치에는 미리보기가 나타나지 않습니다. 간편 모드 중에는 교통 패널에 버스 정류장만 표시됩니다.",
    "hotkey.toggle.desc": "간편 모드(버스 정류장 미리보기)를 켜거나 끕니다.",
    "warn.selectStation": "사용 가능한 버스 정류장이 없습니다",
    "warn.unsupportedLine": "이 모드는 버스 노선에서만 동작합니다.",
  },
  Pl: {
    "master.desc": "Tryb łatwy działa wewnątrz narzędzia linii. Gdy rysujesz linię autobusową, przytrzymaj kursor w miejscu około 0,2 s, a gra pokaże podgląd przystanku na chodniku; stawia go dopiero Twoje własne lewe kliknięcie, a węzeł trasy przypina się do tego przystanku. Nic nie jest stawiane automatycznie i mod nigdy nie klika za Ciebie; miejsce odrzucone przez grę nie pokazuje podglądu. Przy włączonym trybie łatwym panel transportu pokazuje wyłącznie przystanki autobusowe.",
    "hotkey.toggle.desc": "Włącza lub wyłącza tryb łatwy (podgląd przystanków autobusowych).",
    "warn.selectStation": "Brak dostępnego przystanku autobusowego",
    "warn.unsupportedLine": "Ten mod działa tylko z liniami autobusowymi.",
  },
  Pt: {
    "master.desc": "O modo fácil age dentro da ferramenta de linha. Enquanto desenha uma linha de ônibus, mantenha o cursor parado por cerca de 0,2 s e o jogo mostra a pré-visualização do ponto na calçada; quem o coloca é o seu clique esquerdo, e o ponto de passagem se prende a esse ponto. Nada é colocado automaticamente e o mod nunca clica botões por você; um local que o jogo recusa não mostra pré-visualização. Com o modo fácil ativo, o painel de transporte lista apenas pontos de ônibus.",
    "hotkey.toggle.desc": "Ativa ou desativa o modo fácil (pré-visualização de pontos de ônibus).",
    "warn.selectStation": "Nenhum ponto de ônibus disponível",
    "warn.unsupportedLine": "Este mod funciona só com linhas de ônibus.",
  },
  Ru: {
    "master.desc": "Простой режим работает внутри инструмента линии. Когда вы ведёте автобусную линию и держите курсор неподвижно около 0,2 с, игра показывает предпросмотр остановки на тротуаре; ставит её именно ваш левый клик, а путевая точка привязывается к этой остановке. Ничего не размещается автоматически, и мод никогда не нажимает кнопки за вас; там, где игра отказывает, предпросмотр не появляется. Пока простой режим включён, панель транспорта показывает только автобусные остановки.",
    "hotkey.toggle.desc": "Включает или выключает простой режим (предпросмотр автобусных остановок).",
    "warn.selectStation": "Нет доступных автобусных остановок",
    "warn.unsupportedLine": "Этот мод работает только с автобусными линиями.",
  },
};

const src = fs.readFileSync(P, "utf8");
const lines = src.split(/\r?\n/);

// 按"行号"划函数（早先用字符偏移判断，行尾不统一就会漂，把条目算到隔壁语言去）
const fnLines = [];
for (let i = 0; i < lines.length; i++) {
  const mh = /private static Dictionary<string, string> (\w+)\(\)/.exec(lines[i]);
  if (mh) fnLines.push({ i, name: mh[1] });
}
function fnAtLine(idx) {
  let cur = null;
  for (const f of fnLines) { if (f.i <= idx) cur = f.name; else break; }
  return cur;
}

let replaced = {};
for (let li = 0; li < lines.length; li++) {
  const km = /^\s*\{ "([^"]+)",\s*".*$/.exec(lines[li]);
  if (!km) continue;
  const key = km[1];
  const fn = fnAtLine(li);
  if (!fn || !T[fn] || !T[fn][key]) continue;
  lines[li] = '                { "' + key + '", ' + JSON.stringify(T[fn][key]) + " },";
  replaced[fn] = (replaced[fn] || 0) + 1;
}
const missing = Object.keys(T).filter((f) => replaced[f] !== 4);
console.log("每语言替换条数：" + JSON.stringify(replaced));
if (missing.length) {
  console.log("!! 这些语言没替换满 4 条：" + missing.join(","));
  process.exit(1);
}
fs.writeFileSync(P, lines.join("\r\n"), "utf8");
console.log("OK 四条文案已按 12 语言重写");

// 0.9.0 需求 4：给 12 份语言字典补「有轨电车（暂未上线）」三个键。
// 只在 policy.desc 那一行之后插入，按所在函数名判语言；跑第二次会先跳过已有 tram.label 的函数。
const fs = require("fs");
const P = "D:/WorkSpace/CS2MOD/BusLineAutoStops/LocaleTable.cs";

const TRAM = {
  En: [
    "Tram stops (not available yet)",
    "Tram stops follow different placement rules than bus stops (a tram line can only be drawn from tram stop to tram stop), so this part is not finished yet. The option stays here, greyed out, and a later update unlocks it.",
    "Tram stops are not available in this version - lazy mode works on bus lines only.",
  ],
  ZhHans: [
    "有轨电车站自动放置（暂未上线）",
    "有轨电车站的落点规则和公交站不一样（电车线路只能从电车站连到电车站），这部分本版还没做完。条目先保留并置灰，后续版本开放。",
    "有轨电车站暂未上线，懒人模式只处理公交线路。",
  ],
  ZhHant: [
    "電車站自動放置（暫未上線）",
    "電車站的落點規則與公車站不同（電車路線只能從電車站連到電車站），這個版本還沒完成。條目先保留並置灰，後續版本開放。",
    "電車站暫未上線，懶人模式只處理公交路線。",
  ],
  De: [
    "Straßenbahnhaltestellen (noch nicht verfügbar)",
    "Straßenbahnhaltestellen folgen anderen Platzierungsregeln als Bushaltestellen (eine Straßenbahnlinie wird von Haltestelle zu Haltestelle gezeichnet). Dieser Teil ist noch nicht fertig; der Punkt bleibt hier grau reserviert und wird später freigeschaltet.",
    "Straßenbahnhaltestellen sind noch nicht verfügbar – der Komfortmodus arbeitet nur mit Buslinien.",
  ],
  Es: [
    "Paradas de tranvía (aún no disponibles)",
    "Las paradas de tranvía siguen reglas distintas a las de autobús (una línea de tranvía se traza de parada en parada). Esta parte aún no está terminada; la opción queda aquí bloqueada y se activará en una actualización futura.",
    "Las paradas de tranvía aún no están disponibles: el modo fácil solo funciona con líneas de autobús.",
  ],
  Fr: [
    "Arrêts de tram (pas encore disponibles)",
    "Les arrêts de tramway ne suivent pas les mêmes règles que les arrêts de bus (une ligne de tram se trace d'arrêt en arrêt). Cette partie n'est pas terminée : l'option reste ici, grisée, et une mise à jour future l'ouvrira.",
    "Les arrêts de tram ne sont pas encore disponibles : le mode facile ne gère que les lignes de bus.",
  ],
  It: [
    "Fermate dei tram (non ancora disponibili)",
    "Le fermate del tram seguono regole diverse da quelle degli autobus (una linea del tram si disegna da fermata a fermata). Questa parte non è ancora pronta: la voce resta qui, disattivata, e un aggiornamento futuro la sbloccherà.",
    "Le fermate dei tram non sono ancora disponibili: la modalità facile gestisce solo le linee degli autobus.",
  ],
  Ja: [
    "路面電車停留所の自動設置（未実装）",
    "路面電車の停留所はバス停と異なる配置ルール（停留所から停留所へ路線を引く）のため、この部分はまだ未完成です。項目はグレーのまま予約し、今後のアップデートで開放します。",
    "路面電車停留所は未実装です。かんたんモードはバス路線のみ対応。",
  ],
  Ko: [
    "전차 정류장 자동 배치(미출시)",
    "전차 정류장은 버스 정류장과 배치 규칙이 다릅니다(전차 노선은 정류장에서 정류장으로만 그릴 수 있음). 이 부분은 아직 완성되지 않았으니 항목은 비활성으로 예약하고 추후 업데이트에서 개방합니다.",
    "전차 정류장은 아직 출시되지 않았습니다. 간편 모드는 버스 노선만 처리합니다.",
  ],
  Pl: [
    "Przystanki tramwajowe (jeszcze niedostępne)",
    "Przystanki tramwajowe mają inne zasady stawiania niż autobusowe (linię tramwajową rysuje się od przystanku do przystanku). Ta część nie jest jeszcze gotowa — opcja pozostaje tu, szara, i odblokuje ją późniejsza aktualizacja.",
    "Przystanki tramwajowe nie są jeszcze dostępne — tryb łatwy działa tylko z liniami autobusowymi.",
  ],
  Pt: [
    "Pontos de bonde (ainda indisponíveis)",
    "Pontos de bonde têm regras de posicionamento diferentes dos pontos de ônibus (a linha de bonde é traçada de ponto em ponto). Esta parte ainda não está pronta; a opção fica aqui bloqueada e uma atualização futura a liberará.",
    "Pontos de bonde ainda não estão disponíveis: o modo fácil só funciona com linhas de ônibus.",
  ],
  Ru: [
    "Трамвайные остановки (ещё недоступны)",
    "У трамвайных остановок другие правила размещения, чем у автобусных (линию трамвая проводят от остановки к остановке). Эта часть ещё не готова — пункт остаётся здесь неактивным, его откроет будущее обновление.",
    "Трамвайные остановки ещё недоступны — простой режим работает только с автобусными линиями.",
  ],
};

let src = fs.readFileSync(P, "utf8");
const lines = src.split(/\r?\n/);

// 找出每个函数名的起始行
const fnStart = [];
for (let i = 0; i < lines.length; i++) {
  const m = /private static Dictionary<string, string> (\w+)\(\)/.exec(lines[i]);
  if (m) fnStart.push({ i, name: m[1] });
}
function fnOf(lineIdx) {
  let cur = null;
  for (const f of fnStart) if (f.i <= lineIdx) cur = f.name; else break;
  return cur;
}

const out = [];
let inserted = 0;
const seen = new Set();
for (let i = 0; i < lines.length; i++) {
  out.push(lines[i]);
  if (!/^\s*\{ "policy\.desc", /.test(lines[i])) continue;
  const fn = fnOf(i);
  if (!TRAM[fn] || seen.has(fn)) {
    if (!TRAM[fn]) console.log("!! policy.desc 落在未知函数：" + fn);
    continue;
  }
  if (seen.has(fn)) continue;
  const t = TRAM[fn];
  const ind = lines[i].match(/^\s*/)[0];
  out.push(ind + '{ "tram.label", ' + JSON.stringify(t[0]) + " },");
  out.push(ind + '{ "tram.desc", ' + JSON.stringify(t[1]) + " },");
  out.push(ind + '{ "warn.tramNotOnline", ' + JSON.stringify(t[2]) + " },");
  seen.add(fn);
  inserted++;
}
if (src.includes('{ "tram.label", ')) {
  console.log("已经有 tram.label，跳过（避免重复插入）");
  process.exit(0);
}
if (inserted !== 12) {
  console.log("!! 期望插进 12 份字典，实际 " + inserted + " —— 不落盘");
  process.exit(1);
}
fs.writeFileSync(P, out.join("\r\n"), "utf8");
console.log("OK 插入 " + inserted + " 份 × 3 键");

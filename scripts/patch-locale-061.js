/**
 * v0.6.1：往 12 份词典里插入「落地方式」相关的 5 个新键（land.label / land.desc /
 * land.auto / land.confirm / hint.confirmLand），锚点是各语言已有的 "policy.delete" 那一行。
 *
 * 为什么用脚本而不是 12 次 Edit：这一层最容易出的事故是"某个语言漏了一个键"，
 * pack-release 的键集一致性检查会因此红；脚本能把"插入 + 逐语言计数 + 术语红线"
 * 一次做完，而且可重复执行（已经有这些键就跳过，不会插第二份）。
 *
 * 术语红线（见 memory/cs2-stop-terminology）：中文值里不许出现「站台 / 月台」。
 * 另外 C# 字符串字面量里不能有裸 ASCII 双引号 —— 英文用 ' ' 引号，法/西/俄用 « »。
 */
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const file = path.join(root, "LocaleTable.cs");
let src = fs.readFileSync(file, "utf8");

/** 每个函数名对应一份词典；anchor 是该语言里 policy.delete 的整行（唯一锚点）。 */
const T = {
  En: {
    "land.label": "After the cursor rests",
    "land.desc": "What the mod does once the preview has rested on a legal spot for 0.1 s. 'Place automatically' presses the place button for you, so stops appear one after another as you follow the road. 'Wait for my click' never presses anything: the preview stays a preview and the mod only flashes a reminder that this spot is ready - the stop appears when you press the left mouse button yourself, exactly like the base game.",
    "land.auto": "Place automatically",
    "land.confirm": "Wait for my click",
    "hint.confirmLand": "Spot ready: press the left mouse button to place the stop",
  },
  ZhHans: {
    "land.label": "光标停住以后",
    "land.desc": "预览在合法落点停满 0.1 秒之后，模组做哪一步。选「自动放置」时模组替你按下放置键，沿路走一路放，站点一个一个自己排好。选「等我点击」时模组一个键都不代按：预览就停在预览状态，只在左上角闪一句这里可以放，站点要你自己按左键才会落地，和原版完全一样。",
    "land.auto": "自动放置",
    "land.confirm": "等我点击",
    "hint.confirmLand": "落点已就绪：按鼠标左键放置站点",
  },
  ZhHant: {
    "land.label": "游標停住之後",
    "land.desc": "預覽在合法落點停滿 0.1 秒之後，模組做哪一步。選「自動放置」時模組替你按下放置鍵，沿路走一路放，站點一個一個自己排好。選「等我點擊」時模組一個鍵都不代按：預覽就停在預覽狀態，只在左上角閃一句這裡可以放，站點要你自己按左鍵才會落地，與原版完全相同。",
    "land.auto": "自動放置",
    "land.confirm": "等我點擊",
    "hint.confirmLand": "落點已就緒：按滑鼠左鍵放置站點",
  },
  De: {
    "land.label": "Nach dem Ruhen des Cursors",
    "land.desc": "Was die Mod tut, wenn die Vorschau 0,1 s auf einer erlaubten Stelle ruht. 'Automatisch platzieren' drückt den Platzieren-Knopf für dich - die Haltestellen erscheinen einer nach der anderen entlang der Straße. 'Auf meinen Klick warten' drückt nichts: die Vorschau bleibt eine Vorschau, die Mod zeigt nur blinkend an, dass diese Stelle bereit ist - die Haltestelle kommt erst, wenn du selbst die linke Maustaste drückst, genau wie im Basispiel.",
    "land.auto": "Automatisch platzieren",
    "land.confirm": "Auf meinen Klick warten",
    "hint.confirmLand": "Stelle bereit: Linke Maustaste drücken, um die Haltestelle zu setzen",
  },
  Es: {
    "land.label": "Al detenerse el cursor",
    "land.desc": "Qué hace el mod cuando la vista previa lleva 0,1 s sobre un lugar permitido. «Colocar automáticamente» pulsa el botón de colocación por ti, así las paradas aparecen una tras otra siguiendo la calzada. «Esperar mi clic» no pulsa nada: la vista previa sigue siendo vista previa y el mod solo parpadea avisando de que el lugar está listo; la parada aparece cuando pulsas tú el botón izquierdo, igual que en el juego base.",
    "land.auto": "Colocar automáticamente",
    "land.confirm": "Esperar mi clic",
    "hint.confirmLand": "Lugar listo: pulsa el botón izquierdo para colocar la parada",
  },
  Fr: {
    "land.label": "Quand le curseur s'arrête",
    "land.desc": "Ce que fait le mod après 0,1 s d'immobilité de l'aperçu sur un emplacement autorisé. « Placement automatique » appuie sur le bouton de pose à votre place : les arrêts s'alignent le long de la route. « Attendre mon clic » n'appuie sur rien : l'aperçu reste un aperçu et le mod signale juste en clignotant que l'emplacement est prêt ; l'arrêt n'apparaît que quand vous cliquez du bouton gauche, exactement comme en jeu de base.",
    "land.auto": "Placement automatique",
    "land.confirm": "Attendre mon clic",
    "hint.confirmLand": "Emplacement prêt : cliquez du bouton gauche pour poser l'arrêt",
  },
  It: {
    "land.label": "Dopo l'arresto del cursore",
    "land.desc": "Cosa fa il mod quando l'anteprima resta 0,1 s su un punto consentito. 'Posizionamento automatico' preme il pulsante di posizionamento al posto tuo: le fermate compaiono una dopo l'altra lungo la strada. 'Attendi il mio clic' non preme nulla: l'anteprima resta anteprima e il mod lampeggia soltanto per dire che il punto è pronto; la fermata compare quando premi tu il tasto sinistro, esattamente come nel gioco base.",
    "land.auto": "Posizionamento automatico",
    "land.confirm": "Attendi il mio clic",
    "hint.confirmLand": "Punto pronto: premi il tasto sinistro per posizionare la fermata",
  },
  Ja: {
    "land.label": "カーソルが止まったあと",
    "land.desc": "プレビューが設置可能な上で 0.1 秒静止したあと、MOD がどう動くかを決めます。「自動で設置」は MOD が代わりに設置ボタンを押すので、道路をたどるように停留所が並みます。「自分のクリックを待つ」はどんなボタンも押しません：プレビューはプレビューのまま残り、この場所が準備できているという点滅表示だけ出ます。停留所は自分で左クリックしたときに置かれ、原版と同じ動きです。",
    "land.auto": "自動で設置",
    "land.confirm": "自分のクリックを待つ",
    "hint.confirmLand": "設置準備完了：左クリックで停留所を設置します",
  },
  Ko: {
    "land.label": "커서가 멈춘 뒤",
    "land.desc": "미리보기가 설치 가능한 자리에서 0.1초 머문 뒤 모드가 무엇을 할지 정합니다. '자동 설치'는 모드가 대신 설치 버튼을 눌러 주므로 도로를 따라 정류장이 차례로 놓입니다. '내 클릭 대기'는 어떤 버튼도 누르지 않습니다: 미리보기는 그대로 미리보기로 남고, 이 자리가 준비됐다는 점멸 알림만 표시됩니다. 정류장은 직접 왼쪽 클릭을 눌러야 놓이며, 원본 게임과 똑같습니다.",
    "land.auto": "자동 설치",
    "land.confirm": "내 클릭 대기",
    "hint.confirmLand": "자리 준비됨: 왼쪽 클릭으로 정류장을 설치하세요",
  },
  Pl: {
    "land.label": "Po zatrzymaniu kursora",
    "land.desc": "Co mod robi, gdy podgląd poleży 0,1 s w dozwolonym miejscu. 'Ustawiaj automatycznie' wciska przycisk stawiania za Ciebie - przystanki pojawiają się jeden po drugim wzdłuż drogi. 'Czekaj na mój klik' niczego nie wciska: podgląd pozostaje podglądem, a mod tylko miga, że to miejsce jest gotowe; przystanek pojawia się, gdy sam klikniesz lewym przyciskiem - dokładnie jak w podstawowej grze.",
    "land.auto": "Ustawiaj automatycznie",
    "land.confirm": "Czekaj na mój klik",
    "hint.confirmLand": "Miejsce gotowe: kliknij lewym przyciskiem, aby ustawić przystanek",
  },
  Pt: {
    "land.label": "Depois que o cursor parar",
    "land.desc": "O que o mod faz quando a prévia fica 0,1 s sobre um local permitido. 'Colocar automaticamente' aperta o botão de colocação por você: os pontos aparecem um após o outro ao longo da rua. 'Esperar meu clique' não aperta nada: a prévia continua prévia e o mod apenas pisca avisando que o local está pronto; o ponto aparece quando você clica com o botão esquerdo, igual ao jogo original.",
    "land.auto": "Colocar automaticamente",
    "land.confirm": "Esperar meu clique",
    "hint.confirmLand": "Local pronto: clique com o botão esquerdo para colocar o ponto",
  },
  Ru: {
    "land.label": "После остановки курсора",
    "land.desc": "Что делает мод, когда превью полежит 0,1 с на разрешённом месте. «Ставить автоматически» нажимает кнопку установки за вас: остановки появляются одна за другой вдоль дороги. «Ждать моего клика» ничего не нажимает: превью остаётся превью, а мод лишь мигает, показывая, что место готово; остановка появится, когда вы сами нажмёте левую кнопку мыши - точь-в-точь как в базовой игре.",
    "land.auto": "Ставить автоматически",
    "land.confirm": "Ждать моего клика",
    "hint.confirmLand": "Место готово: нажмите левую кнопку мыши, чтобы поставить остановку",
  },
};

const ORDER = ["land.label", "land.desc", "land.auto", "land.confirm", "hint.confirmLand"];

let inserted = 0;
let skipped = 0;
const problems = [];

for (const fn of Object.keys(T)) {
  const at = src.indexOf("Dictionary<string, string> " + fn + "()");
  if (at < 0) { problems.push(fn + "：找不到词典函数"); continue; }
  const nextAt = src.indexOf("private static Dictionary<string, string>", at + 10);
  const end = nextAt < 0 ? src.length : nextAt;
  let body = src.slice(at, end);

  if (body.indexOf('"land.label"') >= 0) { skipped++; continue; }

  // 锚点：该语言 policy.delete 那一整行（每份词典只有一处）
  const anchorRe = /^(\s*)\{ "policy\.delete", .* \},$/m;
  const m = anchorRe.exec(body);
  if (!m) { problems.push(fn + "：没有 policy.delete 锚点行"); continue; }
  const indent = m[1];
  let add = "";
  for (const k of ORDER) {
    const v = T[fn][k];
    if (/"/.test(v)) { problems.push(fn + "." + k + "：值里有裸双引号"); continue; }
    add += "\n" + indent + "{ \"" + k + "\", " + JSON.stringify(v) + " },";
  }
  const newBody = body.slice(0, m.index + m[0].length) + add + body.slice(m.index + m[0].length);
  src = src.slice(0, at) + newBody + src.slice(end);
  inserted++;
}

if (problems.length) {
  console.error("停止，未写入：\n  " + problems.join("\n  "));
  process.exit(1);
}

// 复核：12 份都要有这 5 个键，中文两份的值里不许出现「站台/月台」
const bad = [];
for (const fn of Object.keys(T)) {
  const at = src.indexOf("Dictionary<string, string> " + fn + "()");
  const nextAt = src.indexOf("private static Dictionary<string, string>", at + 10);
  const body = src.slice(at, nextAt < 0 ? src.length : nextAt);
  for (const k of ORDER) {
    if (body.indexOf('{ "' + k + '", ') < 0) bad.push(fn + " 缺 " + k);
  }
  if (fn === "ZhHans" || fn === "ZhHant") {
    const kre = /\{ "([^"]+)", "([^"]*)" \}/g;
    let mm;
    while ((mm = kre.exec(body))) {
      if (/站台|月台/.test(mm[2])) bad.push(fn + "." + mm[1] + " 出现「站台/月台」");
    }
  }
}
if (bad.length) {
  console.error("写入后自检失败：\n  " + bad.join("\n  "));
  process.exit(1);
}

fs.writeFileSync(file, src);
console.log("词典已更新：插入 " + inserted + " 份，跳过（已有）" + skipped + " 份，共 5 个新键");

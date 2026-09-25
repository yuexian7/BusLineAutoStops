import { ModRegistrar } from "cs2/modding";
import { bindValue, useValue, trigger } from "cs2/api";
import { Localized, useLocalization } from "cs2/l10n";
import React, { useEffect, useMemo, useState } from "react";

// 绑定组与后端 UiCommandSystem.kGroup 一致
const uiState$ = bindValue<string>(
  "BusLineAutoStops",
  "GetUiState",
  '{"enabled":false,"seq":0,"warningKey":""}'
);

type UiState = {
  enabled: boolean;
  seq: number;
  warningKey: string;
  modeKind?: number;
};

const btnStyle = (active: boolean): React.CSSProperties => ({
  width: "40rem",
  height: "40rem",
  borderRadius: "4rem",
  background: active ? "rgba(255, 200, 40, 0.85)" : "rgba(42, 55, 83, 0.92)",
  border: active ? "1rem solid #ffe08a" : "1rem solid rgba(255,255,255,0.15)",
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  cursor: "pointer",
  margin: "2rem",
  pointerEvents: "auto",
});

const warnStyle: React.CSSProperties = {
  maxWidth: "260rem",
  marginTop: "4rem",
  padding: "6rem 10rem",
  background: "rgba(120, 20, 20, 0.92)",
  color: "#fff",
  fontSize: "12rem",
  borderRadius: "4rem",
  pointerEvents: "none",
};

/** 与 LocaleTable.PanelKey / BlgKeys.PanelKey 一致（前端只引用，不自建词典） */
const panelKey = (slug: string) =>
  `Options.BUSLINEAUTOSTOPS.${slug}[BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod.Panel.${slug}]`;

const BTN_TOGGLE = panelKey("BTN_TOGGLE");

/** 线路创建语义图标：站台条 + 路径节点 */
const RouteIcon = ({ active }: { active: boolean }) => (
  <svg width="28" height="28" viewBox="0 0 28 28" xmlns="http://www.w3.org/2000/svg">
    <rect x="3" y="8" width="10" height="4" rx="1" fill={active ? "#3a2a00" : "#d0d8e0"} />
    <rect x="3" y="16" width="10" height="4" rx="1" fill={active ? "#3a2a00" : "#d0d8e0"} />
    <circle cx="18" cy="10" r="3" fill={active ? "#3a2a00" : "#9ad0ff"} />
    <circle cx="24" cy="18" r="3" fill={active ? "#3a2a00" : "#9ad0ff"} />
    <path d="M18 10 L24 18" stroke={active ? "#3a2a00" : "#9ad0ff"} strokeWidth="2" fill="none" />
  </svg>
);

const App = () => {
  const raw = useValue(uiState$);
  // useLocalization：走游戏 l10n（UILocalizationManager.Translate）⇒ BLG 可拦
  const strings = useLocalization();
  const [state, setState] = useState<UiState>({ enabled: false, seq: 0, warningKey: "" });
  const [warningKey, setWarningKey] = useState("");

  useEffect(() => {
    try {
      const parsed = JSON.parse(String(raw ?? "")) as UiState;
      setState(parsed);
      setWarningKey(parsed.warningKey || "");
    } catch {
      /* ignore */
    }
  }, [raw]);

  useEffect(() => {
    if (!warningKey) return;
    const t = window.setTimeout(() => setWarningKey(""), 3000);
    return () => window.clearTimeout(t);
  }, [warningKey]);

  const btnTitle = useMemo(() => {
    const bag = strings as Record<string, unknown> | undefined | null;
    const v = bag ? bag[BTN_TOGGLE] : undefined;
    return typeof v === "string" && v && v !== BTN_TOGGLE ? v : "BusLine AutoStops";
  }, [strings]);

  const onClick = () => {
    try {
      trigger("BusLineAutoStops", "CallToggleLazy", "");
    } catch (e) {
      console.warn("BusLine AutoStops toggle failed", e);
    }
  };

  return (
    <div
      style={{
        position: "absolute",
        top: "8rem",
        left: "8rem",
        display: "flex",
        flexDirection: "column",
        alignItems: "flex-start",
        pointerEvents: "none",
        zIndex: 20,
      }}
    >
      <div role="button" title={btnTitle} style={btnStyle(!!state.enabled)} onClick={onClick}>
        <RouteIcon active={!!state.enabled} />
      </div>
      {warningKey ? (
        <div style={warnStyle}>
          {/* 关键：Localized 触发 Translate，BLG 才能机翻；禁止把译文当字符串直塞 */}
          <Localized id={warningKey} />
        </div>
      ) : null}
    </div>
  );
};

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", () => <App />);
};

export default register;

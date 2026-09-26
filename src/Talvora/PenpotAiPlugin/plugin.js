const TALVORA_UI_URL = "http://127.0.0.1:7676/penpot-ai/index.html";

penpot.ui.open(
  "Talvora AI",
  `${TALVORA_UI_URL}?theme=${encodeURIComponent(penpot.theme)}`,
  { width: 440, height: 720 }
);

function send(type, payload = {}) {
  penpot.ui.sendMessage({
    source: "talvora-ai",
    type,
    ...payload,
  });
}

function selectedShapes() {
  return Array.from(penpot.selection ?? []);
}

function requireSelection() {
  const selection = selectedShapes();
  if (!selection.length) {
    throw new Error("Once Penpot'ta bir veya daha fazla katman sec.");
  }
  return selection;
}

function shapeSummary(shape) {
  const fill =
    Array.isArray(shape.fills) && shape.fills.length
      ? shape.fills[0]?.fillColor ?? null
      : null;

  return {
    id: shape.id,
    name: shape.name || shape.type,
    type: shape.type,
    width: Math.round(shape.width * 10) / 10,
    height: Math.round(shape.height * 10) / 10,
    x: Math.round(shape.x * 10) / 10,
    y: Math.round(shape.y * 10) / 10,
    fill,
    componentInstance:
      typeof shape.isComponentInstance === "function"
        ? shape.isComponentInstance()
        : false,
  };
}

function sendSelection() {
  const selection = selectedShapes();
  send("selection", {
    page: penpot.currentPage?.name ?? null,
    file: penpot.currentFile?.name ?? null,
    count: selection.length,
    items: selection.slice(0, 12).map(shapeSummary),
  });
}

function flattenShapes(shapes) {
  const output = [];
  const visit = (shape) => {
    output.push(shape);
    if (Array.isArray(shape.children)) {
      shape.children.forEach(visit);
    }
  };
  shapes.forEach(visit);
  return output;
}

function solidFillColor(shape) {
  if (!Array.isArray(shape.fills) || !shape.fills.length) {
    return null;
  }

  const fill = shape.fills.find((item) => item?.fillColor);
  return fill?.fillColor ?? null;
}

function tokenNameForColor(color) {
  const normalized = String(color)
    .trim()
    .replace(/^#/, "")
    .replace(/[^a-zA-Z0-9]/g, "")
    .toLowerCase();
  return `color.auto_${normalized || "unknown"}`;
}

async function inspectSelection() {
  const selection = requireSelection();
  send("inspection", {
    summary: {
      file: penpot.currentFile?.name ?? null,
      page: penpot.currentPage?.name ?? null,
      selectionCount: selection.length,
      items: selection.map(shapeSummary),
    },
  });
  send("result", {
    level: "success",
    title: "Secim okundu",
    detail: `${selection.length} katman Talvora tarafindan incelendi.`,
  });
}

async function premiumPolish() {
  const selection = requireSelection();
  let changed = 0;

  for (const shape of flattenShapes(selection)) {
    if (shape.type === "group" || shape.type === "text") {
      continue;
    }

    if ("borderRadius" in shape) {
      shape.borderRadius = Math.max(Number(shape.borderRadius || 0), 18);
    }

    if ("shadows" in shape) {
      shape.shadows = [
        {
          style: "drop-shadow",
          offsetX: 0,
          offsetY: 12,
          blur: 32,
          spread: -8,
          hidden: false,
          color: { color: "#162033", opacity: 0.16 },
        },
      ];
    }

    if ("strokes" in shape && (!shape.strokes || shape.strokes.length === 0)) {
      shape.strokes = [
        {
          strokeColor: "#FFFFFF",
          strokeOpacity: 0.5,
          strokeStyle: "solid",
          strokeWidth: 1,
          strokeAlignment: "inner",
        },
      ];
    }

    changed += 1;
  }

  send("result", {
    level: "success",
    title: "Premium dokunus uygulandi",
    detail: `${changed} katmanda radius, stroke ve yumusak golge sistemi guncellendi.`,
  });
  sendSelection();
}

async function createMobileCopy() {
  const [source, ...rest] = requireSelection();
  if (rest.length || source.type !== "board") {
    throw new Error("Mobil kopya icin tek bir board/frame sec.");
  }

  const clone = source.clone();
  const parent = source.parent ?? penpot.currentPage?.root;
  if (!parent || typeof parent.appendChild !== "function") {
    throw new Error("Secili board icin gecerli bir parent bulunamadi.");
  }

  parent.appendChild(clone);
  clone.name = `${source.name} / Mobile`;
  clone.resize(390, 844);
  clone.x = source.x + source.width + 80;
  clone.y = source.y;
  clone.borderRadius = Math.max(Number(clone.borderRadius || 0), 28);

  penpot.selection = [clone];
  send("result", {
    level: "success",
    title: "Mobil kopya olusturuldu",
    detail: `${clone.name} 390 x 844 olarak secimin yanina yerlestirildi.`,
  });
  sendSelection();
}

async function createComponent() {
  const selection = requireSelection();
  if (selection.some((shape) => shape.isComponentInstance?.())) {
    throw new Error("Secimde zaten component instance'i var. Once normal katmanlari sec.");
  }

  const baseName = selection[0]?.name || "Selection";
  const component = penpot.library.local.createComponent(selection);
  component.name = `Talvora / ${baseName}`;
  const mainInstance = component.mainInstance();
  if (mainInstance) {
    penpot.selection = [mainInstance];
  }

  send("result", {
    level: "success",
    title: "Component olusturuldu",
    detail: `${component.name} yerel Penpot library'ye eklendi.`,
  });
  sendSelection();
}

async function extractColorTokens() {
  const selection = requireSelection();
  const shapes = flattenShapes(selection);
  const catalog = penpot.library.local.tokens;
  let tokenSet = catalog.sets.find(
    (set) => set.name === "Talvora AI Colors"
  );

  if (!tokenSet) {
    tokenSet = catalog.addSet({
      name: "Talvora AI Colors",
      active: true,
    });
  } else if (!tokenSet.active) {
    tokenSet.active = true;
  }

  const tokenByName = new Map(
    tokenSet.tokens.map((token) => [token.name, token])
  );
  let created = 0;
  let applied = 0;

  for (const shape of shapes) {
    const color = solidFillColor(shape);
    if (!color || typeof shape.applyToken !== "function") {
      continue;
    }

    const name = tokenNameForColor(color);
    let token = tokenByName.get(name);
    if (!token) {
      token = tokenSet.addToken({
        type: "color",
        name,
        value: color,
      });
      tokenByName.set(name, token);
      created += 1;
    }

    shape.applyToken(token, ["fill"]);
    applied += 1;
  }

  send("result", {
    level: "success",
    title: "Renkler token'a donusturuldu",
    detail: `${created} yeni token olusturuldu; ${applied} katmana token baglandi.`,
  });
}

async function exportHandoff() {
  const selection = requireSelection();
  const html = penpot.generateMarkup(selection, { type: "html" });
  const css = penpot.generateStyle(selection, {
    type: "css",
    withPrelude: true,
    includeChildren: true,
  });
  const fontFaces = await penpot.generateFontFaces(selection);

  send("handoff", {
    html,
    css: `${fontFaces}\n\n${css}`,
    selection: selection.map(shapeSummary),
  });
  send("result", {
    level: "success",
    title: "Developer handoff hazir",
    detail: "HTML ve CSS Penpot'un kendi kod uretecinden alindi.",
  });
}

async function openPrototype() {
  penpot.openViewer();
  send("result", {
    level: "success",
    title: "Prototype viewer acildi",
    detail: "Penpot viewer yeni sekmede acildi.",
  });
}

async function runCommand(command) {
  switch (command) {
    case "inspect":
      return inspectSelection();
    case "premium":
      return premiumPolish();
    case "mobile":
      return createMobileCopy();
    case "component":
      return createComponent();
    case "tokens":
      return extractColorTokens();
    case "handoff":
      return exportHandoff();
    case "prototype":
      return openPrototype();
    default:
      throw new Error(`Bilinmeyen Talvora komutu: ${command}`);
  }
}

async function runPrompt(prompt) {
  const value = String(prompt ?? "").trim();
  if (!value) {
    throw new Error("Bir komut veya tasarim istegi yaz.");
  }

  const unique = detectPromptCommands(value);
  if (!unique.length) {
    if (selectedShapes().length) {
      await inspectSelection();
      send("result", {
        level: "info",
        title: "Talep inceleme modunda ele alindi",
        detail:
          "Serbest metinde dogrudan bir donusturme niyeti bulunmadi; secim guvenli inceleme modunda analiz edildi.",
      });
      return;
    }

    send("result", {
      level: "info",
      title: "Talebi analiz ettim",
      detail:
        "Uygulanacak katman bulunamadi. Penpot'ta bir board veya katman secip istegini dogal cumleyle tekrar yazabilirsin.",
    });
    return;
  }

  for (const command of unique) {
    await runCommand(command);
  }
}

function normalizeIntentText(value) {
  return String(value ?? "")
    .toLocaleLowerCase("tr-TR")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/ı/g, "i")
    .replace(/[^a-z0-9+#%]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

const INTENT_RULES = [
  {
    command: "premium",
    terms: [
      "premium", "profesyonel", "modern", "sik", "estetik", "kaliteli",
      "guzellestir", "iyilestir", "gelistir", "duzelt", "toparla",
      "yenile", "modernlestir", "cilala", "parlat", "polish", "golge",
      "shadow", "radius", "yuvarla", "daha iyi", "daha guzel",
    ],
  },
  {
    command: "mobile",
    terms: [
      "mobil", "mobile", "telefon", "phone", "responsive", "kucuk ekran",
      "dikey ekran", "android", "ios", "390", "mobil uyumlu",
    ],
  },
  {
    command: "component",
    terms: [
      "component", "komponent", "bilesen", "componentlestir",
      "komponentlestir", "bilesenlestir", "reusable",
      "tekrar kullanilabilir", "parcalara ayir", "parcala",
    ],
  },
  {
    command: "tokens",
    terms: [
      "token", "design system", "tasarim sistemi", "renk sistemi", "tema",
      "theme", "palet", "palette", "tipografi", "typography", "spacing",
      "renkleri sistemlestir",
    ],
  },
  {
    command: "handoff",
    terms: [
      "handoff", "developer", "gelistirici", "html", "css", "frontend",
      "react", "koda cevir", "kodunu cikar", "kod uret", "export code",
    ],
  },
  {
    command: "prototype",
    terms: [
      "prototype", "prototip", "prototiple", "viewer", "onizle",
      "etkilesim", "akis", "flow", "tikla", "gecis", "demo",
    ],
  },
  {
    command: "inspect",
    terms: [
      "incele", "inspect", "analiz", "selection", "secim", "degerlendir",
      "audit", "kontrol", "sorun", "hata", "bak",
    ],
  },
];

function detectPromptCommands(prompt) {
  const text = normalizeIntentText(prompt);
  const commands = [];

  for (const rule of INTENT_RULES) {
    if (rule.terms.some((term) => text.includes(term))) {
      commands.push(rule.command);
    }
  }

  return [...new Set(commands)];
}

async function handleMessage(message) {
  if (!message || typeof message !== "object") {
    return;
  }

  if (message.type === "request-state") {
    send("theme", { theme: penpot.theme });
    sendSelection();
    return;
  }

  if (message.type === "run-command") {
    await runCommand(message.command);
    return;
  }

  if (message.type === "run-prompt") {
    await runPrompt(message.prompt);
  }
}

penpot.ui.onMessage((message) => {
  void handleMessage(message).catch((error) => {
    send("result", {
      level: "error",
      title: "Talvora komutu tamamlanamadi",
      detail: error instanceof Error ? error.message : String(error),
    });
  });
});

penpot.on("selectionchange", () => sendSelection());
penpot.on("pagechange", () => sendSelection());
penpot.on("themechange", (theme) => send("theme", { theme }));

send("theme", { theme: penpot.theme });
sendSelection();

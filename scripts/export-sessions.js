// Export Claude Code session transcripts (.jsonl) to readable Markdown.
// Usage: node export-sessions.js <transcriptDir> <outDir>
const fs = require("fs");
const path = require("path");

const [, , srcDir, outDir] = process.argv;

const sessions = [
  ["d554852e", "01-setup-and-brainstorm", "01", "Phases 0–1: handoff review, repo skeleton, brainstorm (two runs)"],
  ["10a6796d", "02-triage", "02", "Phase 1b: brainstorm triage, fact checks"],
  ["158153a7", "02b-triage-parallel", "02b", "Phase 1b: a second copy of the triage session, started at the same moment by session 01; left after one answer"],
  ["fb10e28b", "03-spec", "03", "Phase 2: spec draft and challenge review"],
  ["5f89a958", "04-architecture-and-slices", "04", "Phase 3: vertical slices, notifications page, architecture and ADRs"],
  ["d1e87886", "05-s1-foundation-and-auth", "05", "S1: foundation and auth"],
  ["747f7d6b", "06-s2-admin", "06", "S2: admin area and audit log"],
  ["d28edfd9", "07-s3a-events", "07", "S3a: event ingestion"],
  ["4220b7ce", "08-s3b-alerts", "08", "S3b: alerts and notifications page"],
  ["45977b9f", "09-s4-delivery", "09", "S4: email and Slack delivery"],
  ["ab9f3c53", "10a-s6-planning-first-start", "10a", "First start of the S5/S6 session; restarted after one message"],
  ["4abd817a", "10-s5-and-s6", "10", "S5 market movements, S6 hardening"],
  ["93660086", "11-wrap-up", "11", "Phase Z: prompts export, retrospective, README"],
];

const SKIP_TOOLS = new Set(["mcp__ccd_turn__update_plan", "mcp__ccd_session__mark_chapter", "ToolSearch"]);

const localTime = (ts) =>
  new Date(ts).toLocaleString("sv-SE", { timeZone: "Europe/Budapest" }).slice(0, 16);

const stripReminders = (s) =>
  s.replace(/<system-reminder>[\s\S]*?<\/system-reminder>/g, "").trim();

const textOf = (content) =>
  typeof content === "string"
    ? content
    : (content || []).filter((c) => c.type === "text").map((c) => c.text).join("\n");

const quote = (s) => s.split("\n").map((l) => "> " + l).join("\n");

const details = (summary, body) =>
  `<details><summary>${summary}</summary>\n\n${body}\n\n</details>`;

function toolLine(c) {
  const i = c.input || {};
  const short = (s) => String(s).replace(/\s+/g, " ").slice(0, 160);
  switch (c.name) {
    case "Bash":
    case "PowerShell":
      return `${c.name}: ${short(i.description || i.command)}`;
    case "Read":
    case "Write":
    case "Edit":
      return `${c.name}: \`${i.file_path}\``;
    case "Grep":
    case "Glob":
      return `${c.name}: \`${short(i.pattern)}\``;
    default:
      return c.name.replace(/^mcp__[^_]+(__)?/, "");
  }
}

function renderToolUse(c) {
  const i = c.input || {};
  if (c.name === "Agent")
    return `**Claude → subagent** (${i.subagent_type || "general-purpose"}): ${i.description || ""}\n\n` +
      details("Subagent prompt", i.prompt || "");
  if (c.name === "ExitPlanMode")
    return `**Claude proposes a plan:**\n\n` + details("Plan", i.plan || "");
  if (c.name === "AskUserQuestion")
    return `**Claude asks:**\n\n` + (i.questions || []).map((q) =>
      `- ${q.question}\n` + (q.options || []).map((o) => `  - *${o.label}*: ${o.description}`).join("\n")).join("\n");
  if (c.name === "mcp__ccd_turn__wrap_up")
    return `**Claude wrap-up:** ${i.recap || ""}` + (i.ask ? `\n\n**Question:** ${i.ask}` : "");
  if (c.name === "mcp__ccd_session__start_session")
    return `**Claude starts/offers a session:** ${i.title || i.proposal_id || ""}` +
      (i.prompt ? "\n\n" + details("Brief for the new session", i.prompt + (i.background ? "\n\nBackground:\n" + i.background : "")) : "");
  return null; // ordinary tool: one line
}

function renderToolResult(name, c) {
  const t = textOf(c.content);
  if (/^The user doesn't want to proceed/.test(t)) {
    const m = t.match(/the user said:\n([\s\S]*)/);
    return `**User rejected the ${name} call**` + (m ? ":\n\n" + quote(stripReminders(m[1])) : ".");
  }
  if (name === "AskUserQuestion") return "**User answered:**\n\n" + quote(stripReminders(t));
  if (name === "ExitPlanMode") return /approved/i.test(t) ? "*Plan approved by the user.*" : "**Plan feedback:**\n\n" + quote(stripReminders(t));
  if (name === "Agent") return details("Subagent report", stripReminders(t));
  return null;
}

for (const [id, file, num, phase] of sessions) {
  const src = fs.readdirSync(srcDir).find((f) => f.startsWith(id) && f.endsWith(".jsonl"));
  const L = fs.readFileSync(path.join(srcDir, src), "utf8").split("\n").filter(Boolean).map((l) => JSON.parse(l));
  const title = (L.filter((o) => o.type === "custom-title").pop() || {}).customTitle || "";
  const stamped = L.filter((o) => o.timestamp && (o.type === "user" || o.type === "assistant"));
  const toolNames = {};
  const out = [];
  let pendingTools = [];
  const flushTools = () => {
    if (pendingTools.length) out.push(pendingTools.map((t) => `- ${t}`).join("\n"));
    pendingTools = [];
  };

  for (const o of L) {
    if (o.isSidechain || o.isMeta) continue;
    if (o.type === "user" && typeof o.message?.content === "string") {
      flushTools();
      if (o.origin?.kind === "peer" && o.origin.handbackReportText) {
        out.push(details(`Subagent report (${o.origin.name || "subagent"}, returned in the background)`, o.origin.handbackReportText));
        continue;
      }
      const t = stripReminders(o.message.content);
      if (!t) continue;
      if (/^<task-notification>/.test(t)) { out.push("*(background task finished)*"); continue; }
      if (o.isCompactSummary) { out.push(details("Context was compacted (summary)", t)); continue; }
      const who = o.origin?.kind === "human" ? "User" : "Session input (not typed by the user)";
      const att = L.filter((a) => a.parentUuid === o.uuid && a.attachment?.filename).map((a) => a.attachment.filename);
      out.push(`---\n\n### ${who} · ${localTime(o.timestamp)}\n\n${quote(t)}` +
        (att.length ? `\n\nAttachments: ${att.map((a) => "`" + path.basename(a) + "`").join(", ")}` : ""));
    } else if (o.type === "user" && Array.isArray(o.message?.content)) {
      for (const c of o.message.content) {
        if (c.type === "text" && o.origin?.kind === "human") {
          flushTools();
          const t = stripReminders(c.text);
          if (t) out.push(`---\n\n### User · ${localTime(o.timestamp)}\n\n${quote(t)}`);
        }
        if (c.type !== "tool_result") continue;
        const r = renderToolResult(toolNames[c.tool_use_id], c);
        if (r) { flushTools(); out.push(r); }
      }
    } else if (o.type === "assistant") {
      for (const c of o.message.content) {
        if (c.type === "text" && c.text.trim()) { flushTools(); out.push(c.text.trim()); }
        if (c.type !== "tool_use") continue;
        toolNames[c.id] = c.name;
        if (SKIP_TOOLS.has(c.name)) continue;
        const r = renderToolUse(c);
        if (r) { flushTools(); out.push(r); } else pendingTools.push(toolLine(c));
      }
    }
  }
  flushTools();

  const head = [
    `# Session ${num} – ${phase}`,
    "",
    `| | |`, `|---|---|`,
    `| Tool | Claude Code (desktop app) |`,
    `| Session title | ${title} |`,
    `| Session id | \`${src.replace(".jsonl", "")}\` |`,
    `| Time (Europe/Budapest) | ${localTime(stamped[0].timestamp)} – ${localTime(stamped[stamped.length - 1].timestamp)} |`,
    "",
    "Exported from the raw `.jsonl` transcript with `scripts/export-sessions.js`. Kept: every user message (verbatim, Hungarian as typed), Claude's text replies, plans, questions and answers, subagent prompts and reports, rejected tool calls with the user's reason. Shortened: other tool calls are one line each; their output and Claude's hidden reasoning are left out. Subagents' own transcripts are not included, only their prompt and report.",
    "",
  ];
  fs.writeFileSync(path.join(outDir, file + ".md"), head.join("\n") + "\n" + out.join("\n\n") + "\n");
  console.log(file, (fs.statSync(path.join(outDir, file + ".md")).size / 1024).toFixed(0) + " KB");
}

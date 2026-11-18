// The demo page: sends through the public API with the demo's sender key, then follows the notification.
let key = null;
let watching = null;

const $ = (id) => document.getElementById(id);
const text = (tag, value, className) => {
  const el = document.createElement(tag);
  el.textContent = value;
  if (className) el.className = className;
  return el;
};

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", "X-Api-Key": key, ...(options.headers ?? {}) },
  });
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.detail ?? body?.title ?? `The API answered ${response.status}.`);
  return body;
}

function variablesFor(template, name) {
  const now = new Date();
  return template === "order-shipped"
    ? { name, orderNo: `SIP-${now.getTime().toString().slice(-5)}`, carrier: "Yurtiçi Kargo", trackingNo: `YK${now.getTime().toString().slice(-9)}` }
    : { name, changedAt: now.toLocaleString("tr-TR", { timeZone: "Europe/Istanbul" }) };
}

async function send(event) {
  event.preventDefault();
  const form = new FormData(event.target);
  const button = event.target.querySelector("button");
  $("error").hidden = true;
  button.disabled = true;
  try {
    const template = form.get("template");
    const notification = await api("/api/notifications", {
      method: "POST",
      headers: { "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify({
        recipient: { id: form.get("recipient"), email: form.get("email") },
        channel: form.get("channel"),
        template,
        language: form.get("language"),
        variables: variablesFor(template, form.get("name")),
      }),
    });
    follow(notification.id, form.get("recipient"));
  } catch (error) {
    $("error").textContent = error.message;
    $("error").hidden = false;
  } finally {
    button.disabled = false;
  }
}

function follow(id, recipient) {
  clearInterval(watching);
  let ticks = 0;
  const tick = async () => {
    const n = await api(`/api/notifications/${id}`).catch(() => null);
    if (!n) return;
    const status = $("status");
    status.replaceChildren(text("span", n.status, `status ${n.status}`), document.createTextNode(` · ${n.channel} · “${n.subject}”`));
    $("history").replaceChildren(...(n.history ?? []).map((h) =>
      text("li", `${new Date(h.occurredAt).toLocaleTimeString()} · ${h.status}${h.detail ? ` · ${h.detail}` : ""}`)));
    const where = $("where");
    if (n.status === "Delivered") {
      where.replaceChildren();
      if (n.channel === "Email") {
        where.append("Delivered. Open the ");
        const link = text("a", "mailbox");
        link.href = "/mail/";
        link.target = "_blank";
        link.rel = "noopener";
        where.append(link, " to read the email.");
      } else {
        where.append("Delivered to the in-app inbox below.");
      }
      where.hidden = false;
      inbox(recipient);
    }
    if (["Delivered", "Suppressed", "DeadLettered"].includes(n.status) || ++ticks > 60) clearInterval(watching);
  };
  $("where").hidden = true;
  tick();
  watching = setInterval(tick, 1000);
}

async function inbox(recipient) {
  const page = await api(`/api/recipients/${encodeURIComponent(recipient)}/inbox`).catch(() => null);
  if (!page) return;
  const box = $("inbox");
  box.className = "";
  if (page.items.length === 0) {
    box.replaceChildren(text("p", "Nothing in this inbox yet.", "muted"));
    return;
  }
  box.replaceChildren(text("p", `${page.unread} unread`, "muted"), ...page.items.slice(0, 5).map((item) => {
    const row = text("div", "", "item");
    row.append(text("strong", item.subject, item.readAt ? "" : "unread"), text("pre", item.text));
    return row;
  }));
}

(async () => {
  const demo = await fetch("/api/demo").then((r) => r.json()).catch(() => null);
  if (!demo) {
    $("key").textContent = "This installation has no public demo key.";
    $("send").querySelector("button").disabled = true;
    return;
  }
  key = demo.senderApiKey;
  $("key").textContent = key;
  $("send").addEventListener("submit", send);
})();

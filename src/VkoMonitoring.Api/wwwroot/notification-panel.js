import { incidentStatuses } from "./incident-model.js";
import { timestamp } from "./dashboard-model.js";

export function createNotificationPanel({ element, request, getSession, onUnauthorized, onOpenIncident }) {
  const host = document.getElementById("notifications-panel"), button = document.getElementById("open-notifications");
  let rows = [], userKey = "", generation = 0;
  const feedback = element("p", "section-note"); feedback.setAttribute("role", "status");
  const list = element("div", "notification-list"), markAll = element("button", "button secondary", "Отметить всё прочитанным"); markAll.type = "button";
  host.append(element("h2", "", "Уведомления"), element("p", "section-note", "Проблемы и восстановления по доступным линиям."), markAll, feedback, list);
  const storageKey = () => `vko-notifications-read:${userKey}`;
  function readSet() { try { return new Set(JSON.parse(localStorage.getItem(storageKey()) || "[]")); } catch { return new Set(); } }
  function save(set) { localStorage.setItem(storageKey(), JSON.stringify([...set].slice(-1000))); }
  function eventId(row) { return `${row.incidentId}:${row.status}:${row.recoveredAtUtc || row.detectedAtUtc}`; }
  function render() {
    const read = readSet(), unread = rows.filter(row => !read.has(eventId(row))).length;
    button.textContent = unread ? `Уведомления (${unread})` : "Уведомления";
    list.replaceChildren();
    for (const row of rows) {
      const id = eventId(row), card = element("article", `notification-card ${read.has(id) ? "read" : "unread"}`);
      const recovered = ["Resolved", "Closed"].includes(row.status);
      card.append(element("h3", "", recovered ? `Связь восстановлена · ${row.schoolName}` : `Проблема линии · ${row.schoolName}`),
        element("p", "", `${row.lineName} · ${row.providerName || "Поставщик не указан"}`),
        element("p", "", `${row.incidentNumber} · ${incidentStatuses[row.status] || row.status} · ${row.title}`),
        element("p", "section-note", timestamp(recovered ? row.recoveredAtUtc || row.closedAtUtc : row.detectedAtUtc)));
      const open = element("button", "text-button", "Открыть инцидент"); open.type = "button"; open.addEventListener("click", () => { read.add(id); save(read); render(); onOpenIncident(row.schoolId); }); card.append(open); list.append(card);
    }
    feedback.textContent = rows.length ? `Событий: ${rows.length}. Непрочитанных: ${unread}.` : "За последние 90 дней событий нет.";
  }
  markAll.addEventListener("click", () => { const read = readSet(); rows.forEach(row => read.add(eventId(row))); save(read); render(); });
  async function refresh() {
    const session = getSession(), current = generation; if (!session.token) return;
    const from = new Date(Date.now() - 90 * 86400000).toISOString(), to = new Date(Date.now() + 86400000).toISOString();
    try { rows = await request(`/api/incidents?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=500`, { token: session.token }); if (current !== generation) return; render(); }
    catch (error) { if (error.status === 401) onUnauthorized(); else feedback.textContent = "Не удалось загрузить уведомления."; }
  }
  function configure(user) { userKey = user?.login || "anonymous"; button.hidden = false; }
  function open() { host.hidden = false; host.scrollIntoView({ behavior: "smooth", block: "start" }); refresh(); }
  function clear() { generation++; rows = []; userKey = ""; host.hidden = true; button.hidden = true; button.textContent = "Уведомления"; list.replaceChildren(); feedback.textContent = ""; }
  return { configure, refresh, open, clear };
}

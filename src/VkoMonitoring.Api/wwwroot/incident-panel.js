import { incidentStatuses, nextIncidentStatuses, incidentPermissions, incidentDuration } from "./incident-model.js";
import { dateValue, periodRange, periodQuery } from "./history-model.js";
import { timestamp } from "./dashboard-model.js";

export function createIncidentPanel({ element, request, getSession, onUnauthorized }) {
  const host = document.getElementById("incidents-panel");
  let schools = [], permissions = incidentPermissions(null), selected = "", generation = 0, listVersion = 0, detailVersion = 0, writing = false;
  let listController, detailController;
  const button = (text, action, className = "button secondary") => { const node = element("button", className, text); node.type = "button"; node.addEventListener("click", action); return node; };
  const filters = element("form", "incident-filters"), list = element("div"), detail = element("section", "incident-detail"); detail.hidden = true;
  const feedback = element("p", "section-note"); feedback.setAttribute("role", "status");
  const schoolFilter = element("select"); schoolFilter.id = "incident-school-filter";
  const statusFilter = element("select"); statusFilter.id = "incident-status-filter";
  statusFilter.append(new Option("Все статусы", ""), ...Object.entries(incidentStatuses).map(([value, text]) => new Option(text, value)));
  function field(parent, text, input, id) { if (id) input.id = id; const label = element("label", "directory-field"); label.append(element("span", "", text), input); parent.append(label); return input; }
  const from = element("input"), to = element("input"); from.type = to.type = "date"; from.required = to.required = true;
  function resetDates() { const now = new Date(), start = new Date(now); start.setDate(start.getDate() - 29); from.value = dateValue(start); to.value = dateValue(now); }
  resetDates();
  field(filters, "Школа инцидента", schoolFilter); field(filters, "Статус инцидента", statusFilter);
  field(filters, "Начало периода инцидентов", from, "incident-from"); field(filters, "Конец периода инцидентов", to, "incident-to");
  const apply = element("button", "button secondary", "Применить фильтры"); apply.type = "submit"; filters.append(apply);
  filters.addEventListener("submit", event => { event.preventDefault(); closeDetail(); refresh(); });
  const creation = element("details", "incident-create"); creation.append(element("summary", "", "Создать инцидент вручную"));
  const createForm = element("form", "directory-form"), createSchool = element("select"), createLine = element("select");
  field(createForm, "Школа нового инцидента", createSchool, "incident-create-school"); field(createForm, "Линия нового инцидента", createLine, "incident-create-line");
  const type = element("input"), title = element("input"), description = element("textarea");
  for (const input of [type, title, description]) input.required = true;
  type.maxLength = title.maxLength = 200; description.maxLength = 4000; description.rows = 4;
  field(createForm, "Тип проблемы", type, "incident-create-type"); field(createForm, "Название проблемы", title, "incident-create-title"); field(createForm, "Описание проблемы", description, "incident-create-description");
  const createButton = element("button", "button primary", "Создать инцидент"); createButton.type = "submit"; createForm.append(createButton);
  const createFeedback = element("p", "section-note"); createFeedback.setAttribute("role", "status"); creation.append(createForm, createFeedback);
  function updateCreateLines() {
    const old = createLine.value, lines = schools.find(school => school.schoolId === createSchool.value)?.lines || [];
    createLine.replaceChildren(...lines.map(line => new Option(line.name, line.lineId)));
    if (lines.some(line => line.lineId === old)) createLine.value = old;
    createButton.disabled = writing || !lines.length; createSchool.disabled = createLine.disabled = writing;
  }
  createSchool.addEventListener("change", updateCreateLines);
  function closeDetail() { detailVersion++; detailController?.abort(); selected = ""; detail.hidden = true; detail.replaceChildren(); }
  function validSession(session, current) { return session.epoch === getSession().epoch && generation === current; }
  function failure(error) {
    if (error.status === 401) { onUnauthorized(); return ""; }
    return error.status === 429 ? "Слишком много запросов. Повторите через минуту." : error.status === 404 || error.status === 403 ? "Инцидент недоступен вашей учётной записи." : error.status === 409 ? "Данные изменились или действие недопустимо. Обновите карточку. На линии может уже быть открытый инцидент." : "Не удалось выполнить запрос. Проверьте поля и соединение.";
  }
  async function refresh() {
    const session = getSession(), current = generation, version = ++listVersion;
    if (!session.token) return;
    listController?.abort(); listController = new AbortController();
    let query;
    try { query = new URLSearchParams(periodQuery(periodRange("custom", from.value, to.value))); }
    catch (error) { feedback.textContent = error.message; list.replaceChildren(); return; }
    if (schoolFilter.value) query.set("schoolId", schoolFilter.value);
    if (statusFilter.value) query.set("status", statusFilter.value);
    query.set("limit", "1000"); feedback.textContent = "Загрузка инцидентов…"; list.replaceChildren();
    try {
      const rows = await request(`/api/incidents?${query}`, { token: session.token, signal: listController.signal });
      if (!validSession(session, current) || version !== listVersion) return;
      feedback.textContent = `Инцидентов: ${rows.length}. Период относится к времени начала проблемы.${rows.length === 1000 ? " Показаны последние 1000. Уменьшите период для полной выборки." : ""}`;
      if (!rows.length) { list.append(element("p", "section-note", "По выбранным условиям инцидентов нет.")); return; }
      const scroll = element("div", "table-scroll"), table = element("table"), head = element("thead"), body = element("tbody"), header = element("tr");
      for (const text of ["Номер / проблема", "Школа / линия", "Статус", "Начало / длительность", "Ответственный"]) { const cell = element("th", "", text); cell.scope = "col"; header.append(cell); }
      head.append(header);
      for (const row of rows) {
        const tr = element("tr"), name = element("td"), school = element("td");
        name.append(button(row.incidentNumber, () => open(row.incidentId), "text-button"), element("span", "secondary-text", row.title));
        school.append(element("strong", "", row.schoolName), element("span", "secondary-text", `${row.lineName} · ${row.providerName || "Поставщик не указан"}`));
        tr.append(name, school, element("td", "", incidentStatuses[row.status] || row.status), element("td", "", `${timestamp(row.startedAtUtc)} · ${incidentDuration(row.durationSeconds)}`), element("td", "", row.assignedTo || "Не назначен")); body.append(tr);
      }
      table.append(head, body); scroll.append(table); list.append(scroll);
    } catch (error) { if (validSession(session, current) && version === listVersion) feedback.textContent = failure(error); }
  }
  async function open(id) {
    if (writing || !getSession().token) return;
    selected = id; const session = getSession(), current = generation, version = ++detailVersion;
    detailController?.abort(); detailController = new AbortController(); detail.hidden = false;
    detail.replaceChildren(element("p", "section-note", "Загрузка карточки инцидента…"));
    try {
      const data = await request(`/api/incidents/${id}`, { token: session.token, signal: detailController.signal });
      if (!validSession(session, current) || version !== detailVersion || selected !== id) return;
      showDetails(data); detail.scrollIntoView({ behavior: "smooth", block: "start" });
    } catch (error) { if (validSession(session, current) && version === detailVersion) detail.replaceChildren(button("Закрыть карточку инцидента", closeDetail), element("p", "section-note", failure(error))); }
  }
  async function mutate(path, method, body, target) {
    if (writing) return false;
    const session = getSession(), current = generation; writing = true;
    for (const control of host.querySelectorAll("input,textarea,select,button")) control.disabled = true;
    target.textContent = "Сохраняем…";
    try {
      const result = await request(path, { method, body: { ...body, actor: "web" }, token: session.token });
      if (!validSession(session, current)) return false;
      target.textContent = "Сохранено."; return result || true;
    } catch (error) { if (validSession(session, current)) target.textContent = failure(error); return false; }
    finally { if (validSession(session, current)) { writing = false; for (const control of host.querySelectorAll("input,textarea,select,button")) control.disabled = false; updateCreateLines(); } }
  }
  createForm.addEventListener("submit", async event => {
    event.preventDefault(); if (!createLine.value) return;
    const epoch = getSession().epoch;
    const result = await mutate("/api/incidents", "POST", { schoolId: createSchool.value, lineId: createLine.value, problemType: type.value.trim(), title: title.value.trim(), description: description.value.trim(), startedAtUtc: null, assignedTo: null, comment: null }, createFeedback);
    if (!result) return;
    type.value = title.value = description.value = ""; creation.open = false;
    await refresh(); if (epoch === getSession().epoch && result.incidentId) await open(result.incidentId);
  });
  function showDetails({ incident: row, history }) {
    detail.replaceChildren(button("Закрыть карточку инцидента", closeDetail), element("h3", "", `${row.incidentNumber} · ${row.title}`), button("Обновить инцидент", () => open(row.incidentId)));
    for (const text of [`${row.schoolName} · ${row.lineName} · ${row.providerName || "Поставщик не указан"}`, `${incidentStatuses[row.status]} · ${row.source === "Automatic" ? "Автоматический" : "Ручной"} · ${row.problemType}`, row.description, `Ответственный: ${row.assignedTo || "не назначен"}`, `Начало: ${timestamp(row.startedAtUtc)} · длительность: ${incidentDuration(row.durationSeconds)}`, `Обнаружен: ${timestamp(row.detectedAtUtc)}`, `Передан: ${timestamp(row.sentToProviderAtUtc)} · восстановлен: ${timestamp(row.recoveredAtUtc)} · закрыт: ${timestamp(row.closedAtUtc)}`]) detail.append(element("p", "incident-text", text));
    const appeal = element("details", "incident-create"), appealSummary = element("summary", "", "Обращение провайдеру");
    const appealText = element("textarea"); appealText.rows = 12; appealText.maxLength = 8000;
    appealText.value = `Поставщику: ${row.providerName || "не указан"}\nПо организации: ${row.schoolName}\nЛиния: ${row.lineName}\nИнцидент: ${row.incidentNumber}\nДата начала: ${timestamp(row.startedAtUtc)}\nТип проблемы: ${row.problemType}\n\nОписание:\n${row.description}\n\nПросим проверить качество и доступность услуги, сообщить причину нарушения и срок восстановления.`;
    const confirmed = element("input"); confirmed.type = "checkbox"; const confirmLabel = element("label", "admin-check"); confirmLabel.append(confirmed, element("span", "", "Я проверил текст, адресата и факты перед передачей"));
    const appealFeedback = element("p", "section-note"); appealFeedback.setAttribute("role", "status");
    const copyAppeal = button("Скопировать текст", async () => { if (!confirmed.checked) { appealFeedback.textContent = "Сначала проверьте текст и подтвердите проверку."; return; } try { await navigator.clipboard.writeText(appealText.value); appealFeedback.textContent = "Текст скопирован."; } catch { appealFeedback.textContent = "Не удалось скопировать автоматически. Выделите текст вручную."; } });
    const printAppeal = button("Печать / сохранить PDF", () => { if (!confirmed.checked) { appealFeedback.textContent = "Сначала проверьте текст и подтвердите проверку."; return; } const frame = document.createElement("iframe"); frame.hidden = true; document.body.append(frame); const doc = frame.contentDocument; doc.open(); doc.write(`<title>${escapeHtml(row.incidentNumber)}</title><style>body{font:14pt Arial;line-height:1.5;margin:25mm}h1{font-size:20pt}pre{white-space:pre-wrap;font:inherit}</style><h1>Обращение провайдеру</h1><pre>${escapeHtml(appealText.value)}</pre>`); doc.close(); frame.contentWindow.focus(); frame.contentWindow.print(); setTimeout(() => frame.remove(), 1000); });
    appeal.append(appealSummary, element("p", "section-note", "Текст создан по данным инцидента без ИИ. Отредактируйте и подтвердите его; затем распечатайте или выберите «Сохранить как PDF» в окне печати."), appealText, confirmLabel, copyAppeal, printAppeal, appealFeedback); detail.append(appeal);
    const changes = element("div", "directory-grid");
    function changeForm(heading, path, method, inputs, body) {
      const form = element("form", "directory-form"); form.append(element("h4", "", heading));
      for (const [label, input] of inputs) field(form, label, input);
      const save = element("button", "button secondary", heading); save.type = "submit";
      const message = element("p", "section-note"); message.setAttribute("role", "status"); form.append(save, message); changes.append(form);
      form.addEventListener("submit", async event => { event.preventDefault(); const epoch = getSession().epoch; if (await mutate(`/api/incidents/${row.incidentId}/${path}`, method, body(), message)) { await refresh(); if (epoch === getSession().epoch && selected === row.incidentId) await open(row.incidentId); } });
    }
    const next = nextIncidentStatuses(row.status);
    if (permissions.status && next.length) {
      const status = element("select"), comment = element("textarea"); comment.maxLength = 4000;
      status.append(...next.map(value => new Option(incidentStatuses[value], value)));
      changeForm("Изменить статус", "status", "PUT", [["Новый статус", status], ["Комментарий к статусу", comment]], () => ({ status: status.value, comment: comment.value.trim() || null }));
    }
    if (permissions.assign) {
      const assigned = element("input"); assigned.maxLength = 200; assigned.value = row.assignedTo || "";
      changeForm("Назначить ответственного", "assignment", "PUT", [["Ответственный по инциденту", assigned]], () => ({ assignedTo: assigned.value.trim() || null, comment: null }));
    }
    if (permissions.comment) {
      const comment = element("textarea"); comment.maxLength = 4000; comment.required = true;
      changeForm("Добавить комментарий", "comments", "POST", [["Текст комментария", comment]], () => ({ comment: comment.value.trim() }));
    }
    detail.append(changes, element("h4", "", "История действий"));
    const actions = { Created: "Создан", CreatedManually: "Создан вручную", CreatedAutomatically: "Создан автоматически", RecoveredAutomatically: "Автоматически восстановлен", StatusChanged: "Статус изменён", Assigned: "Назначен ответственный", AssignmentChanged: "Ответственный изменён", CommentAdded: "Комментарий", AutomaticallyResolved: "Автоматически восстановлен" };
    const entries = element("ol", "incident-history");
    for (const entry of history) { const item = element("li"); item.append(element("p", "incident-text", `${timestamp(entry.occurredAtUtc)} · ${actions[entry.action] || entry.action} · ${entry.actor || "Система"}`)); if (entry.newStatus) item.append(element("p", "incident-text", `${incidentStatuses[entry.previousStatus] || "—"} → ${incidentStatuses[entry.newStatus] || entry.newStatus}`)); if (entry.comment) item.append(element("p", "incident-text", entry.comment)); entries.append(item); }
    detail.append(entries);
  }
  function escapeHtml(value) { return value.replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]); }
  function configure(available, user) {
    const updatedPermissions = incidentPermissions(user?.role);
    if (JSON.stringify(permissions) !== JSON.stringify(updatedPermissions)) closeDetail();
    schools = available; permissions = updatedPermissions; creation.hidden = !permissions.create;
    for (const select of [schoolFilter, createSchool]) {
      const old = select.value;
      select.replaceChildren(...(select === schoolFilter ? [new Option("Все доступные школы", "")] : []), ...schools.map(school => new Option(school.name, school.schoolId)));
      if (schools.some(school => school.schoolId === old)) select.value = old;
    }
    updateCreateLines();
  }
  function clear() { generation++; listVersion++; detailVersion++; listController?.abort(); detailController?.abort(); writing = false; schools = []; selected = ""; list.replaceChildren(); detail.replaceChildren(); detail.hidden = true; feedback.textContent = createFeedback.textContent = ""; type.value = title.value = description.value = ""; creation.open = false; schoolFilter.replaceChildren(); createSchool.replaceChildren(); createLine.replaceChildren(); statusFilter.value = ""; resetDates(); for (const control of host.querySelectorAll("input,textarea,select,button")) control.disabled = false; }
  host.append(element("h2", "", "Инциденты"), element("p", "section-note", "Проблемы доступных вам линий и история их решения. Даты — в часовом поясе вашего устройства."), filters, feedback, list, creation, detail);
  return { configure, refresh, clear, showSchool: id => { schoolFilter.value = id; closeDetail(); refresh(); host.scrollIntoView({ behavior: "smooth" }); } };
}

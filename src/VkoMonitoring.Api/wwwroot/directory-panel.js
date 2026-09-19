import { optionalSpeed } from "./directory-model.js";
import { activationLifetime } from "./activation-model.js";

export function createDirectoryPanel({ element, request, getSession, onUnauthorized, onSaved }) {
  const host = document.getElementById("directory-panel"); host.hidden = true;
  const details = element("details", "directory-editor");
  details.append(element("summary", "", "Школы, линии, контакты и договоры"));
  const note = element("p", "section-note", "Создайте школу, затем её интернет-линию. Компьютеры подключаются отдельно по кодам активации.");
  const schoolSelect = element("select"); schoolSelect.id = "directory-school";
  const lineSelect = element("select"); lineSelect.id = "directory-line";
  const schoolForm = element("form", "directory-form"), lineForm = element("form", "directory-form");
  const schoolFields = {}, lineFields = {};
  const feedback = element("p", "section-note"); feedback.setAttribute("role", "status");
  let schools = [], selectedSchoolId = "", selectedLineId = "", busy = false, generation = 0;
  function field(container, label, control) {
    const wrap = element("label", "directory-field"); wrap.append(element("span", "", label), control); container.append(wrap);
  }
  function fields(container, target, specifications, prefix) {
    for (const [key, label, type, limit] of specifications) {
      const input = element("input"); input.type = type || "text"; input.id = `${prefix}-${key}`;
      if (limit) input.maxLength = limit; if (key === "name") input.required = true;
      if (key.includes("Mbps")) input.inputMode = "decimal";
      target[key] = input; field(container, label, input);
    }
  }
  field(schoolForm, "Выберите школу", schoolSelect);
  fields(schoolForm, schoolFields, [["name", "Название школы", "text", 200], ["districtCity", "Район / город", "text", 200],
    ["address", "Адрес", "text", 1000], ["responsibleName", "Ответственный — ФИО", "text", 200],
    ["responsiblePosition", "Должность", "text", 200], ["responsiblePhone", "Телефон", "tel", 100],
    ["responsibleEmail", "Электронная почта", "email", 254]], "directory-school");
  field(lineForm, "Выберите линию", lineSelect);
  fields(lineForm, lineFields, [["name", "Название линии", "text", 200], ["providerName", "Поставщик", "text", 200],
    ["connectionType", "Тип подключения", "text", 100], ["contractedDownloadMbps", "Download по договору, Мбит/с", "text", 20],
    ["contractedUploadMbps", "Upload по договору, Мбит/с", "text", 20], ["contractNumber", "Номер договора", "text", 200],
    ["contractDate", "Дата договора", "date"]], "directory-line");
  lineFields.lineStatus = element("select");
  for (const [value, label] of [["Primary", "Основная"], ["Backup", "Резервная"], ["Disabled", "Отключена"]])
    lineFields.lineStatus.append(new Option(label, value));
  field(lineForm, "Назначение линии", lineFields.lineStatus);
  const schoolSave = element("button", "button primary", "Сохранить школу"); schoolSave.type = "submit";
  const lineSave = element("button", "button primary", "Сохранить линию"); lineSave.type = "submit";
  const resetSchool = element("button", "text-button", "Загрузить сохранённые данные школы"); resetSchool.type = "button";
  const resetLine = element("button", "text-button", "Загрузить сохранённые данные линии"); resetLine.type = "button";
  schoolForm.append(schoolSave, resetSchool); lineForm.append(lineSave, resetLine);
  const schoolSection = element("section"); schoolSection.append(element("h3", "", "Школа и ответственный"), schoolForm);
  const lineSection = element("section"); lineSection.append(element("h3", "", "Интернет-линия и договор"), lineForm);
  const grid = element("div", "directory-grid"); grid.append(schoolSection, lineSection);
  const activationForm = element("form", "directory-form activation-form");
  activationForm.append(element("h3", "", "Подключить компьютер"), element("p", "section-note", "Выберите сохранённые школу и линию выше. Для каждого компьютера выдайте отдельный одноразовый код и введите его в окне активации установленного агента."));
  const lifetime = element("input"); lifetime.id = "activation-lifetime"; lifetime.type = "number"; lifetime.min = "5"; lifetime.max = "1440"; lifetime.step = "1"; lifetime.required = true; lifetime.value = "30";
  field(activationForm, "Срок действия кода, минут", lifetime);
  const issue = element("button", "button primary", "Выдать код активации"); issue.type = "submit";
  const activationFeedback = element("p", "section-note"); activationFeedback.setAttribute("role", "status");
  const output = element("div", "activation-result"); output.hidden = true;
  const code = element("input"); code.id = "activation-code"; code.readOnly = true; code.autocomplete = "off"; code.setAttribute("aria-label", "Код активации");
  const binding = element("p", "section-note"), expiry = element("p", "section-note");
  const copy = element("button", "button secondary", "Скопировать код"); copy.type = "button";
  const hide = element("button", "text-button", "Скрыть код"); hide.type = "button";
  output.append(binding, code, expiry, copy, hide);
  activationForm.append(issue, activationFeedback, output);
  const registry = element("section", "activation-registry");
  registry.append(element("h3", "", "Реестр кодов активации"));
  const refreshCodes = element("button", "button secondary", "Обновить реестр"); refreshCodes.type = "button";
  const registryFeedback = element("p", "section-note"); registryFeedback.setAttribute("role", "status");
  const registryTable = element("div", "table-wrap");
  registry.append(refreshCodes, registryFeedback, registryTable);
  details.append(note, grid, feedback, activationForm, registry); host.append(details);
  let issuance = 0, expirationTimer = null;
  function clearCode() { issuance++; clearTimeout(expirationTimer); expirationTimer = null; code.value = ""; output.hidden = true; binding.textContent = ""; expiry.textContent = ""; activationFeedback.textContent = ""; }
  hide.addEventListener("click", clearCode);
  copy.addEventListener("click", async () => {
    const current = issuance;
    try { await navigator.clipboard.writeText(code.value); if (current === issuance) activationFeedback.textContent = "Код скопирован."; }
    catch { if (current === issuance) activationFeedback.textContent = "Не удалось скопировать автоматически. Выделите код и скопируйте вручную."; }
  });
  activationForm.addEventListener("submit", async event => {
    event.preventDefault(); if (busy || !selectedSchoolId || !selectedLineId) return;
    clearCode(); const current = issuance, session = getSession();
    const valid = () => current === issuance && session.epoch === getSession().epoch;
    const school = currentSchool(), line = school?.lines.find(item => item.lineId === selectedLineId);
    if (!line) return;
    try {
      const minutes = activationLifetime(lifetime.value);
      setBusy(true); activationFeedback.textContent = "Выдаём код…";
      const result = await request("/api/activation-codes", { method: "POST", token: session.token,
        body: { schoolId: school.schoolId, lineId: line.lineId, lifetimeMinutes: minutes } });
      if (!valid()) return;
      code.value = result.activationCode; binding.textContent = `${school.name} · ${line.name}`;
      expiry.textContent = `Действует до ${new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" }).format(new Date(result.expiresAtUtc))}. Код показывается только здесь; скрытие не отзывает его. Новый код не отменяет ранее выданные.`;
      output.hidden = false; activationFeedback.textContent = "Код выдан. Один код — один компьютер.";
      await loadCodes();
      expirationTimer = setTimeout(() => { if (valid()) { clearCode(); activationFeedback.textContent = "Срок действия кода истёк. Выдайте новый."; } }, Math.max(0, Date.parse(result.expiresAtUtc) - Date.now()));
    } catch (error) {
      if (!valid()) return;
      if (error.status === 401) { onUnauthorized(); return; }
      activationFeedback.textContent = error.status === 429 ? "Слишком много запросов. Повторите через минуту." : error.status === 404 ? "Школа или линия больше недоступна. Обновите список." : error.detail || "Не удалось выдать код. Проверьте соединение и права доступа.";
    } finally { if (session.epoch === getSession().epoch) setBusy(false); }
  });
  const statusLabels = { Active: "Действует", Used: "Использован", Expired: "Истёк", Revoked: "Отозван" };
  async function loadCodes() {
    const session = getSession(); registryFeedback.textContent = "Загружаем…";
    try {
      const rows = await request("/api/activation-codes?limit=100", { token: session.token });
      if (session.epoch !== getSession().epoch) return;
      const table = element("table", "data-table");
      const head = element("tr"); for (const label of ["Школа и линия", "Выдан", "Действует до", "Статус", "Действие"]) head.append(element("th", "", label));
      const body = element("tbody");
      for (const row of rows) {
        const tr = element("tr");
        tr.append(element("td", "", `${row.schoolName} · ${row.lineName}`), element("td", "", formatDate(row.createdAtUtc)),
          element("td", "", formatDate(row.expiresAtUtc)), element("td", `status-${row.status.toLowerCase()}`, statusLabels[row.status] || row.status));
        const action = element("td");
        if (row.status === "Active") {
          const revoke = element("button", "text-button", "Отозвать"); revoke.type = "button";
          revoke.addEventListener("click", async () => {
            revoke.disabled = true; registryFeedback.textContent = "Отзываем код…";
            try { await request(`/api/activation-codes/${row.activationCodeId}/revoke`, { method: "POST", token: getSession().token }); await loadCodes(); }
            catch (error) { if (error.status === 401) onUnauthorized(); else registryFeedback.textContent = error.detail || "Не удалось отозвать код."; }
          });
          action.append(revoke);
        } else action.textContent = "—";
        tr.append(action); body.append(tr);
      }
      table.append(head, body); registryTable.replaceChildren(table); registryFeedback.textContent = rows.length ? `Показано: ${rows.length}. Сами коды после выдачи не хранятся в открытом виде.` : "Коды ещё не выдавались.";
    } catch (error) { if (error.status === 401) onUnauthorized(); else registryFeedback.textContent = error.detail || "Не удалось загрузить реестр."; }
  }
  function formatDate(value) { return value ? new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" }).format(new Date(value)) : "—"; }
  refreshCodes.addEventListener("click", loadCodes);
  function currentSchool() { return schools.find(school => school.schoolId === selectedSchoolId); }
  function fill(target, data) { for (const [key, input] of Object.entries(target)) input.value = data?.[key] ?? (key === "lineStatus" ? "Primary" : ""); }
  function setBusy(value) {
    busy = value;
    for (const control of host.querySelectorAll("input, select, button")) control.disabled = value;
    if (!value) for (const control of lineForm.querySelectorAll("input,select,button")) control.disabled = !selectedSchoolId;
    issue.disabled = value || !selectedSchoolId || !selectedLineId;
  }
  function updateLines() {
    const lines = currentSchool()?.lines || [];
    lineSelect.replaceChildren(new Option("Новая линия", ""), ...lines.map(line => new Option(line.name, line.lineId)));
    if (selectedLineId && !lines.some(line => line.lineId === selectedLineId)) { selectedLineId = ""; clearCode(); }
    lineSelect.value = selectedLineId; setBusy(busy);
  }
  function fillLine() { fill(lineFields, currentSchool()?.lines.find(line => line.lineId === selectedLineId)); }
  schoolSelect.addEventListener("change", () => {
    clearCode();
    selectedSchoolId = schoolSelect.value; selectedLineId = ""; fill(schoolFields, currentSchool()); updateLines(); fillLine(); feedback.textContent = "";
  });
  lineSelect.addEventListener("change", () => { clearCode(); selectedLineId = lineSelect.value; fillLine(); setBusy(busy); feedback.textContent = ""; });
  resetSchool.addEventListener("click", () => fill(schoolFields, currentSchool())); resetLine.addEventListener("click", fillLine);
  function read(target) { return Object.fromEntries(Object.entries(target).map(([key, input]) => [key, input.value.trim() || null])); }
  async function save(event, kind) {
    event.preventDefault(); if (busy || kind === "line" && !selectedSchoolId) return;
    const session = getSession(), localGeneration = generation;
    const valid = () => session.epoch === getSession().epoch && localGeneration === generation;
    const schoolId = selectedSchoolId, lineId = selectedLineId;
    try {
      const body = read(kind === "school" ? schoolFields : lineFields);
      if (kind === "line") for (const key of ["contractedDownloadMbps", "contractedUploadMbps"]) body[key] = optionalSpeed(lineFields[key].value);
      setBusy(true); feedback.textContent = "Сохраняем…";
      const existing = kind === "school" ? schoolId : lineId;
      const path = kind === "school" ? `/api/schools${schoolId ? `/${schoolId}` : ""}` : `/api/schools/${schoolId}/lines${lineId ? `/${lineId}` : ""}`;
      const result = await request(path, { method: existing ? "PUT" : "POST", body, token: session.token });
      if (!valid()) return;
      if (result?.schoolId) selectedSchoolId = result.schoolId;
      if (result?.lineId) selectedLineId = result.lineId;
      const available = await request("/api/schools", { token: session.token });
      if (!valid()) return;
      render(available, session.user);
      await onSaved(); if (!valid()) return;
      if (kind === "school") fill(schoolFields, currentSchool()); else fillLine();
      feedback.textContent = kind === "school" ? "Школа сохранена." : "Линия и договор сохранены.";
    } catch (error) {
      if (!valid()) return;
      if (error.status === 401) { onUnauthorized(); return; }
      feedback.textContent = error.detail || (error.status ? "Не удалось сохранить. Проверьте поля, права доступа и соединение." : error.message);
    } finally { if (valid()) setBusy(false); }
  }
  schoolForm.addEventListener("submit", event => save(event, "school")); lineForm.addEventListener("submit", event => save(event, "line"));
  function render(available, user) {
    if (!["Administrator", "Regional"].includes(user?.role)) clearCode();
    schools = available; host.hidden = !["Administrator", "Regional"].includes(user?.role) || !document.getElementById("school-detail").hidden;
    schoolSelect.replaceChildren(new Option("Новая школа", ""), ...schools.map(school => new Option(school.name, school.schoolId)));
    if (selectedSchoolId && !currentSchool()) { clearCode(); selectedSchoolId = ""; selectedLineId = ""; fill(schoolFields, null); fill(lineFields, null); }
    schoolSelect.value = selectedSchoolId; updateLines();
    if (!host.hidden) loadCodes();
  }
  function clear() {
    clearCode(); lifetime.value = "30";
    generation++; schools = []; selectedSchoolId = ""; selectedLineId = ""; busy = false;
    host.hidden = true; details.open = false; schoolSelect.replaceChildren(); lineSelect.replaceChildren();
    fill(schoolFields, null); fill(lineFields, null); feedback.textContent = ""; setBusy(false);
    registryTable.replaceChildren(); registryFeedback.textContent = "";
  }
  return { render, clear };
}

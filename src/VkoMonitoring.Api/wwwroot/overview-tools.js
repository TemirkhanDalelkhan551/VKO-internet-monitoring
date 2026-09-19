import { metric, timestamp, summarize } from "./dashboard-model.js";
import { dateValue, periodRange, periodQuery } from "./history-model.js";

export function createOverviewTools({ request, getSession, onUnauthorized, element }) {
  const host = document.getElementById("overview-tools");
  let epoch = 0, schools = [], options = null, controls = null, stats = null, groupStats = null, feedback = null;
  let analyticsVersion = 0, deviceVersion = 0, exportVersion = 0;
  let analyticsController, devicesController, exportController, optionsController;
  let devicesLoading = false, exporting = false;
  const downloads = new Map();
  const button = (text, action) => { const node = element("button", "button secondary", text); node.type = "button"; node.addEventListener("click", action); return node; };
  const label = (text, node, id) => { node.id = id; const caption = element("label", "", text); caption.htmlFor = id; const group = element("div", "report-control"); group.append(caption, node); return group; };
  function select(items) {
    const node = element("select"); for (const [value, text] of items) { const option = element("option", "", text); option.value = value; node.append(option); } return node;
  }
  function notice(text, info = false) { if (!feedback) return; feedback.textContent = text; feedback.hidden = !text; feedback.classList.toggle("info", info); }
  function available() { if (controls) controls.download.disabled = !options || !schools.length || devicesLoading || exporting; }
  function cancelExport() { exportVersion++; exportController?.abort(); exporting = false; notice(""); available(); }
  function clear() {
    epoch++; analyticsVersion++; deviceVersion++; cancelExport();
    for (const controller of [analyticsController, devicesController, optionsController]) controller?.abort();
    for (const [url, timer] of downloads) { clearTimeout(timer); URL.revokeObjectURL(url); } downloads.clear();
    host.replaceChildren(); controls = null; options = null; schools = []; devicesLoading = false;
  }
  function initialise() {
    controls = {};
    const panel = element("section", "detail-section"); panel.append(element("h2", "", "Аналитика и отчёты"),
      element("p", "section-note", "Аналитика всех доступных вам организаций. Поиск и фильтры списка школ ниже не меняют эту сводку. Даты периода — в часовом поясе вашего устройства."));
    const period = element("div", "detail-period");
    controls.preset = select([["day", "Сегодня"], ["week", "7 дней"], ["month", "30 дней"], ["custom", "Свой период"]]); controls.preset.value = "week";
    period.append(label("Период аналитики и отчёта", controls.preset, "overview-period"));
    controls.dates = element("div", "detail-dates"); controls.dates.hidden = true;
    for (const [key, text] of [["from", "Начало периода"], ["to", "Окончание периода"]]) {
      controls[key] = element("input"); controls[key].type = "date"; controls[key].value = dateValue(new Date());
      controls[key].addEventListener("input", cancelExport);
      controls.dates.append(label(text, controls[key], `overview-${key}`));
    }
    controls.dates.append(button("Применить период", loadAnalytics)); period.append(controls.dates);
    controls.preset.addEventListener("change", () => { cancelExport(); controls.dates.hidden = controls.preset.value !== "custom"; if (controls.preset.value !== "custom") loadAnalytics(); });
    stats = element("div", "overview-analytics"); stats.setAttribute("aria-live", "polite");
    const report = element("details", "report-settings"), summary = element("summary", "", "Выгрузить отчёт CSV / XLSX");
    const reportNote = element("p", "section-note", "Выберите школу, компьютеры, статус соединения и столбцы. Сводный отчёт считается по тем же выбранным замерам. Даты и время в файле указаны в UTC. Ограничение — 20 000 замеров; превышение не обрезается.");
    const filters = element("div", "report-filters");
    controls.school = select([["", "Все доступные школы"]]);
    controls.kind = select([["measurements", "Подробные измерения"], ["summary", "Сводка по школам"]]);
    controls.status = select([["", "Все статусы"], ["Online", "На связи (Online)"], ["Degraded", "Сбой измерения (Degraded)"], ["Offline", "Нет соединения (Offline)"]]);
    controls.format = select([["xlsx", "XLSX · Excel"], ["csv", "CSV · UTF-8"]]);
    filters.append(label("Школа для отчёта", controls.school, "report-school"), label("Вид отчёта", controls.kind, "report-kind"),
      label("Статус соединения", controls.status, "report-status"), label("Формат файла", controls.format, "report-format"));
    controls.school.addEventListener("change", () => { cancelExport(); loadDevices(); });
    controls.kind.addEventListener("change", () => { cancelExport(); renderFields(); });
    for (const node of [controls.status, controls.format]) node.addEventListener("change", cancelExport);
    controls.devices = element("fieldset", "report-choices"); controls.devices.append(element("legend", "", "Компьютеры для отчёта"), element("p", "section-note", "Для выбора отдельных компьютеров сначала выберите школу. Без выбора — все доступные компьютеры."));
    controls.fields = element("fieldset", "report-choices");
    controls.download = button("Скачать отчёт", download); controls.download.disabled = true;
    feedback = element("div", "message"); feedback.hidden = true; feedback.setAttribute("role", "status");
    report.append(summary, reportNote, filters, controls.devices, controls.fields, controls.download, feedback);
    groupStats = element("details", "report-settings"); groupStats.append(element("summary", "", "Сводка по районам и поставщикам"));
    panel.append(period, stats, groupStats, report); host.append(panel);
  }
  function renderGroupStats() {
    function groups(key, values) {
      const map = new Map();
      for (const school of schools) for (const value of values(school)) { if (!value) continue; if (!map.has(value)) map.set(value, []); if (!map.get(value).includes(school)) map.get(value).push(school); }
      const table = element("table"), header = element("tr"); for (const text of [key, "Школы", "Устройства", "На связи", "Проблемные линии", "Без свежих замеров"]) header.append(element("th", "", text));
      const body = element("tbody"); for (const [name, items] of [...map].sort((a,b) => a[0].localeCompare(b[0], "ru"))) { const total = summarize(items), row = element("tr"); for (const value of [name, total.schools, total.devices, total.activeDevices, total.problemLines, total.missingLines]) row.append(element("td", "", String(value))); body.append(row); }
      table.append(header, body); return table;
    }
    const wrap = element("div", "group-summary-grid"), district = element("section"), provider = element("section");
    district.append(element("h3", "", "Районы и города"), groups("Район / город", school => [school.districtCity || "Не указан"]));
    provider.append(element("h3", "", "Поставщики"), groups("Поставщик", school => [...new Set(school.lines.filter(line => line.lineStatus !== "Disabled").map(line => line.providerName || "Не указан"))]));
    wrap.append(district, provider); groupStats.replaceChildren(element("summary", "", "Сводка по районам и поставщикам"), wrap);
  }
  function errorMessage(error) {
    if (error.status === 401) { onUnauthorized(); return ""; }
    return error.status === 429 ? "Слишком много запросов. Повторите через минуту." : error.status === 404 ? "Выбранные данные недоступны вашей учётной записи. Обновите список школ." : error.detail || "Не удалось загрузить данные. Проверьте соединение и повторите попытку.";
  }
  function renderFields() {
    if (!options || !controls) return;
    const columns = controls.kind.value === "summary" ? options.summaryColumns : options.measurementColumns;
    controls.fields.replaceChildren(element("legend", "", "Столбцы отчёта"));
    for (const column of columns) {
      const caption = element("label", "report-choice"); const checkbox = element("input"); checkbox.type = "checkbox"; checkbox.value = column.key; checkbox.checked = true;
      checkbox.addEventListener("change", cancelExport); caption.append(checkbox, element("span", "", column.label)); controls.fields.append(caption);
    }
  }
  async function loadDevices() {
    devicesController?.abort(); deviceVersion++; const version = deviceVersion, currentEpoch = epoch, session = getSession();
    const id = controls.school.value;
    controls.devices.replaceChildren(element("legend", "", "Компьютеры для отчёта"));
    if (!id) { devicesLoading = false; controls.devices.append(element("p", "section-note", "Все доступные компьютеры всех доступных школ.")); available(); return; }
    devicesLoading = true; available(); controls.devices.append(element("p", "section-note", "Загрузка компьютеров…"));
    devicesController = new AbortController();
    const valid = () => currentEpoch === epoch && version === deviceVersion && session.epoch === getSession().epoch;
    try {
      const devices = await request(`/api/schools/${id}/devices`, { token: session.token, signal: devicesController.signal });
      if (!valid()) return;
      controls.devices.replaceChildren(element("legend", "", "Компьютеры для отчёта"), element("p", "section-note", "Отметьте один или несколько компьютеров. Если ни один не отмечен — все доступные компьютеры школы."));
      for (const device of devices) {
        const caption = element("label", "report-choice"), checkbox = element("input"); checkbox.type = "checkbox"; checkbox.value = device.deviceId;
        checkbox.addEventListener("change", cancelExport); caption.append(checkbox, element("span", "", `${device.name} · ${device.room || "Кабинет не указан"} · ${device.deviceId}`)); controls.devices.append(caption);
      }
      if (!devices.length) controls.devices.append(element("p", "section-note", "У школы нет доступных устройств."));
      devicesLoading = false;
    } catch (error) { if (!valid()) return; const text = errorMessage(error); if (!valid() || !controls) return; notice(text); controls.devices.append(button("Повторить загрузку компьютеров", loadDevices)); }
    finally { if (valid()) available(); }
  }
  function range() { return periodRange(controls.preset.value, controls.from.value, controls.to.value); }
  async function loadAnalytics() {
    if (!controls || !getSession().token) return;
    analyticsController?.abort(); analyticsVersion++; const version = analyticsVersion, currentEpoch = epoch, session = getSession();
    const valid = () => currentEpoch === epoch && version === analyticsVersion && session.epoch === getSession().epoch;
    stats.replaceChildren(element("p", "section-note", "Загрузка аналитики…"));
    analyticsController = new AbortController();
    try {
      const selectedRange = range();
      const data = await request(`/api/analytics?${periodQuery(selectedRange)}`, { token: session.token, signal: analyticsController.signal });
      if (!valid()) return;
      const grid = element("div", "analytics-grid");
      for (const [text, value, unit, note] of [
        ["Средний Download", data.averageDownloadMbps, "Мбит/с", `Мин. ${metric(data.minimumDownloadMbps)} · макс. ${metric(data.maximumDownloadMbps)}`],
        ["Средний Upload", data.averageUploadMbps, "Мбит/с", `Мин. ${metric(data.minimumUploadMbps)} · макс. ${metric(data.maximumUploadMbps)}`],
        ["Средний Ping", data.averagePingMilliseconds, "мс", `Мин. ${metric(data.minimumPingMilliseconds)} · макс. ${metric(data.maximumPingMilliseconds)}`],
        ["Замеры", data.measurementCount, "", data.measurementCount ? `Проблемных: ${data.problemMeasurementCount} (${metric(data.problemMeasurementPercent)}%)` : "За период данных нет"]]) {
        const card = element("article", "summary-card"); card.append(element("p", "summary-label", text), element("p", "analytics-value", `${metric(value)} ${unit}`), element("p", "summary-note", note)); grid.append(card);
      }
      const dates = new Intl.DateTimeFormat("ru-RU", {year:"numeric",month:"2-digit",day:"2-digit"});
      stats.replaceChildren(element("p", "section-note", `Применённый период: ${dates.format(new Date(selectedRange.from))} — ${dates.format(new Date(Date.parse(selectedRange.to) - 1))}. Обновлено ${timestamp(new Date().toISOString())}.`), grid);
    } catch (error) { if (!valid()) return; const text = error.status ? errorMessage(error) : error.message === "Укажите обе даты периода." || error.message === "Начало периода не может быть позже окончания." ? error.message : "Не удалось загрузить аналитику. Проверьте даты и соединение."; if (controls) stats.replaceChildren(element("p", "message", text), button("Повторить загрузку аналитики", loadAnalytics)); }
  }
  async function download() {
    if (controls.download.disabled) return;
    notice(""); const fields = Array.from(controls.fields.querySelectorAll("input:checked")).map(input => input.value);
    if (!fields.length) { notice("Выберите хотя бы один столбец отчёта."); return; }
    let selectedRange; try { selectedRange = range(); } catch (error) { notice(error.message); return; }
    cancelExport(); const version = exportVersion, currentEpoch = epoch, session = getSession(); exporting = true; available();
    const valid = () => currentEpoch === epoch && version === exportVersion && session.epoch === getSession().epoch;
    exportController = new AbortController();
    notice("Формирование отчёта…", true);
    const query = new URLSearchParams({...selectedRange, kind:controls.kind.value, format:controls.format.value, fields:fields.join(",")});
    if (controls.school.value) query.set("schoolId", controls.school.value);
    if (controls.status.value) query.set("status", controls.status.value);
    const ids = Array.from(controls.devices.querySelectorAll("input:checked")).map(input => input.value);
    if (ids.length) query.set("deviceIds", ids.join(","));
    try {
      const file = await request(`/api/reports/export?${query}`, {token:session.token,signal:exportController.signal,responseType:"file"});
      if (!valid()) return;
      const url = URL.createObjectURL(file.blob), link = element("a"); link.href = url; link.download = file.filename.replace(/[^a-zA-Z0-9._-]/g, "_"); link.hidden = true;
      host.append(link); link.click(); link.remove();
      downloads.set(url, setTimeout(() => { URL.revokeObjectURL(url); downloads.delete(url); }, 60000));
      notice(file.count ? `Отчёт готов. Исходных замеров: ${file.count}.` : "Отчёт готов. По выбранным условиям замеров нет: файл содержит заголовки.", true);
    } catch (error) { if (valid()) notice(errorMessage(error)); }
    finally { if (valid()) { exporting = false; available(); } }
  }
  async function refresh(nextSchools) {
    schools = nextSchools;
    if (!controls) initialise();
    renderGroupStats();
    const selected = controls.school.value;
    controls.school.replaceChildren(...[["", "Все доступные школы"], ...schools.map(school => [school.schoolId, school.name])].map(([value, text]) => { const option = element("option", "", text); option.value = value; return option; }));
    controls.school.value = schools.some(school => school.schoolId === selected) ? selected : "";
    if (selected && controls.school.value !== selected) { cancelExport(); await loadDevices(); }
    if (!options) {
      const currentEpoch = epoch, session = getSession(); optionsController = new AbortController();
      try { const result = await request("/api/reports/options", {token:session.token,signal:optionsController.signal});
        if (currentEpoch !== epoch || session.epoch !== getSession().epoch) return;
        options = result; renderFields();
      } catch (error) { if (currentEpoch !== epoch || session.epoch !== getSession().epoch) return; notice(errorMessage(error)); }
    }
    available(); await loadAnalytics();
  }
  return { refresh, clear };
}

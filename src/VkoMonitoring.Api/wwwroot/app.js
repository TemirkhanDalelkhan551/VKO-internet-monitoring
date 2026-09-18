import { roles, statuses, presenceLabels, lineTypes, accessDescription, filterSchools, summarize, freshnessDescription, schoolMeasurementDescription, lineCountLabel, metric, timestamp } from "./dashboard-model.js";
import { createSchoolPanel } from "./school-panel.js";
import { createOverviewTools } from "./overview-tools.js";
import { createSchoolMap } from "./school-map.js";
import { createDirectoryPanel } from "./directory-panel.js";
import { contractShortfalls } from "./directory-model.js";

const byId = id => document.getElementById(id);
const state = { token: null, user: null, expires: 0, epoch: 0, schools: [], loaded: false, loading: false,
  loadedAt: null, expanded: new Set(), controller: null, expiryTimer: null };

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function message(id, text, info = false) {
  const node = byId(id);
  node.textContent = text;
  node.classList.toggle("info", info);
  node.hidden = !text;
}

class ApiError extends Error {
  constructor(status, detail = "") { super(`HTTP ${status}`); this.status = status; this.detail = detail; }
}

async function request(path, { method = "GET", body, token, signal, responseType = "json" } = {}) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  if (signal?.aborted) abort(); else signal?.addEventListener("abort", abort, { once: true });
  const timer = setTimeout(abort, 20000);
  try {
    const headers = { Accept: "application/json" };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store", credentials: "omit", signal: controller.signal });
    if (!response.ok) {
      let detail = "";
      if ([400, 422].includes(response.status)) { try { const problem = await response.json(); detail = problem.detail || problem.error || Object.values(problem.errors || {}).flat().join(" "); } catch {} }
      throw new ApiError(response.status, detail);
    }
    if (responseType === "file") return { blob: await response.blob(),
      filename: response.headers.get("Content-Disposition")?.match(/filename="?([^";]+)"?/)?.[1] || "vko-report",
      count: Number(response.headers.get("X-Report-Measurement-Count")) };
    return response.status === 204 ? null : await response.json();
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", abort);
  }
}

function endSession(text = "") {
  state.epoch++;
  schoolPanel.clear();
  overviewTools.clear();
  schoolMap.clear();
  directoryPanel.clear();
  state.controller?.abort();
  clearTimeout(state.expiryTimer);
  Object.assign(state, { token: null, user: null, expires: 0, schools: [], loaded: false, loading: false, loadedAt: null });
  state.expanded.clear();
  byId("school-rows").replaceChildren();
  byId("summary").replaceChildren();
  for (const id of ["user-name", "user-role", "user-avatar", "access-description", "school-count", "updated-at", "result-count"]) byId(id).textContent = "";
  byId("district-filter").replaceChildren(element("option", "", "Все районы и города"));
  byId("district-filter").firstChild.value = "";
  byId("search").value = "";
  byId("status-filter").value = "";
  for (const id of ["provider-filter", "connection-filter"]) byId(id).replaceChildren(new Option("Все", ""));
  byId("password").value = "";
  byId("password").type = "password";
  byId("show-password").textContent = "Показать";
  byId("show-password").setAttribute("aria-pressed", "false");
  byId("show-password").setAttribute("aria-label", "Показать пароль");
  message("dashboard-message", "");
  byId("dashboard-view").hidden = true;
  byId("login-view").hidden = false;
  document.title = "Вход · Мониторинг интернета ВКО";
  message("login-message", text, true);
  byId("login").focus();
}

function showDashboard(user) {
  byId("user-name").textContent = user.displayName;
  byId("user-role").textContent = roles[user.role] || "Пользователь";
  byId("user-avatar").textContent = [...(user.displayName || user.login)].slice(0, 1).join("").toLocaleUpperCase("ru");
  byId("access-description").textContent = accessDescription(user);
  byId("login-view").hidden = true;
  byId("dashboard-view").hidden = false;
  document.title = "Школы и линии · Мониторинг интернета ВКО";
  byId("refresh").focus();
}

function badge(status) {
  const [label, color] = statuses[status] || statuses.Unknown;
  return element("span", `badge ${color}`, label);
}

function statusCell(item) {
  const cell = element("td");
  cell.append(badge(item.status));
  if (item.measurementFreshness === "Stale") cell.append(element("span", "secondary-text", "Последнее качество: " + (statuses[item.qualityStatus] || statuses.Unknown)[0]));
  const primaryUnavailable = "primaryLineId" in item && !item.primaryLineId;
  cell.append(element("span", `presence ${!primaryUnavailable && item.agentPresence === "Active" ? "active" : ""}`,
    primaryUnavailable ? "Нет данных основной линии" : presenceLabels[item.agentPresence] || "Связь с агентом неизвестна"));
  return cell;
}

function metricCell(value, unit) {
  const cell = element("td");
  cell.append(element("span", "metric", metric(value)), element("span", "unit", unit));
  return cell;
}

function lineCard(line) {
  const card = element("article", "line-card");
  const top = element("div", "line-top");
  const heading = element("div");
  heading.append(element("h3", "line-name", line.name), element("p", "line-type", `${lineTypes[line.lineStatus] || line.lineStatus} · ${line.providerName || "Поставщик не указан"}`));
  top.append(heading, badge(line.status));
  const metrics = element("dl", "line-metrics");
  const snapshot = line.latestMeasurement;
  for (const [label, value, unit] of [["Download", snapshot?.downloadMbps, "Мбит/с"], ["Upload", snapshot?.uploadMbps, "Мбит/с"], ["Ping", snapshot?.pingMilliseconds, "мс"], ["Jitter", snapshot?.jitterMilliseconds, "мс"], ["Потери", snapshot?.packetLossPercent, "%"]]) {
    const group = element("div"); group.append(element("dt", "", label), element("dd", "", `${metric(value)} ${unit}`)); metrics.append(group);
  }
  const foot = element("div", "line-foot");
  foot.append(element("span", "", `${freshnessDescription(line)} · ${timestamp(snapshot?.measuredAtUtc)}`),
    element("span", "", presenceLabels[line.agentPresence] || "Связь с агентом неизвестна"),
    element("span", "", `Устройств на связи: ${line.activeDeviceCount} из ${line.deviceCount}`));
  if (line.measurementFreshness === "Stale") foot.append(element("span", "", "Последнее качество: " + (statuses[line.qualityStatus] || statuses.Unknown)[0]));
  if (line.contractedDownloadMbps !== null || line.contractedUploadMbps !== null) foot.append(element("span", "", `Договор: ↓ ${metric(line.contractedDownloadMbps)} / ↑ ${metric(line.contractedUploadMbps)} Мбит/с`));
  if (line.contractNumber || line.contractDate) foot.append(element("span", "", `Договор № ${line.contractNumber || "не указан"} · ${line.contractDate || "дата не указана"}`));
  const shortfalls = contractShortfalls(line);
  if (shortfalls.length) foot.append(element("span", "contract-shortfall", `Последний актуальный замер ниже договорной скорости: ${shortfalls.join(", ")}.`));
  card.append(top, metrics, foot);
  return card;
}

function schoolRows(school) {
  const row = element("tr");
  const schoolCell = element("td");
  const button = element("button", "school-button", school.name);
  button.type = "button";
  button.setAttribute("aria-expanded", String(state.expanded.has(school.schoolId)));
  button.setAttribute("aria-controls", `lines-${school.schoolId}`);
  button.addEventListener("click", () => {
    if (state.expanded.has(school.schoolId)) state.expanded.delete(school.schoolId); else state.expanded.add(school.schoolId);
    renderSchools();
    byId(`school-${school.schoolId}`)?.focus();
  });
  button.id = `school-${school.schoolId}`;
  schoolCell.append(button, element("span", "secondary-text", school.districtCity || "Район не указан"), element("span", "secondary-text", `${lineCountLabel(school.lines.length)} · ${state.expanded.has(school.schoolId) ? "Скрыть" : "Показать"} показатели`));
  const open = element("button", "text-button school-open", "Открыть карточку →");
  open.type = "button"; open.id = `open-${school.schoolId}`;
  open.setAttribute("aria-label", `Открыть карточку: ${school.name}`);
  open.addEventListener("click", () => schoolPanel.open(school)); schoolCell.append(open);
  const lineCell = element("td");
  lineCell.append(element("span", "", school.providerName || (school.primaryLineId ? "Поставщик не указан" : "Нет доступной основной линии")),
    element("span", "secondary-text", school.connectionType || "—"));
  const measuredCell = element("td");
  measuredCell.append(element("span", "", timestamp(school.latestMeasurement?.measuredAtUtc)), element("span", "secondary-text", schoolMeasurementDescription(school)));
  const deviceCell = element("td");
  const count = element("span", "device-count", String(school.activeDeviceCount));
  count.append(element("span", "total", ` / ${school.deviceCount}`));
  deviceCell.append(count, element("span", "secondary-text", "на связи / всего"));
  row.append(schoolCell, lineCell, statusCell(school), metricCell(school.latestMeasurement?.downloadMbps, "Мбит/с"),
    metricCell(school.latestMeasurement?.uploadMbps, "Мбит/с"), metricCell(school.latestMeasurement?.pingMilliseconds, "мс"), measuredCell, deviceCell);
  const details = element("tr", "line-details");
  details.id = `lines-${school.schoolId}`;
  details.hidden = !state.expanded.has(school.schoolId);
  const detailsCell = element("td"); detailsCell.colSpan = 8;
  const grid = element("div", "line-grid");
  if (school.lines.length) for (const line of school.lines) grid.append(lineCard(line));
  else grid.append(element("p", "secondary-text", "Линии пока не зарегистрированы."));
  detailsCell.append(grid); details.append(detailsCell);
  return [row, details];
}

function renderSummary() {
  const totals = summarize(state.schools);
  const cards = [["Организации образования", totals.schools, "В вашей области доступа"],
    ["Устройства на связи", `${totals.activeDevices} / ${totals.devices}`, "Активные / зарегистрированные"],
    ["Линии с проблемами", totals.problemLines, "По актуальным замерам"],
    ["Без актуальных замеров", totals.missingLines, "Устаревшие или отсутствующие"]];
  byId("summary").replaceChildren(...cards.map(([label, count, note]) => {
    const card = element("article", "summary-card");
    card.append(element("p", "summary-label", label), element("p", "summary-number", state.loaded ? String(count) : "—"), element("p", "summary-note", note));
    return card;
  }));
}

function renderSchools() {
  const schools = filterSchools(state.schools, byId("search").value, byId("district-filter").value, byId("status-filter").value,
    byId("provider-filter").value, byId("connection-filter").value);
  if (state.loaded) schoolMap.render(schools, state.schools, state.user);
  byId("school-count").textContent = state.loaded ? String(state.schools.length) : "—";
  byId("result-count").textContent = state.loaded ? `Показано: ${schools.length} из ${state.schools.length}` : "Загрузка школ…";
  byId("reset-filters").hidden = !["search", "district-filter", "status-filter", "provider-filter", "connection-filter"].some(id => byId(id).value);
  byId("school-rows").replaceChildren(...schools.flatMap(schoolRows));
  byId("empty-state").hidden = !state.loaded || schools.length > 0;
  byId("empty-title").textContent = state.schools.length ? "Школы не найдены" : "Пока нет доступных школ";
  byId("empty-description").textContent = state.schools.length ? "Измените условия поиска или сбросьте фильтры." : "Школы появятся после регистрации и назначения доступа вашей учётной записи.";
}

function updateDistricts() {
  for (const [id, property, label] of [["provider-filter", "providerName", "Все поставщики"], ["connection-filter", "connectionType", "Все подключения"]]) {
    const selected = byId(id).value;
    const values = [...new Set(state.schools.flatMap(school => school.lines.map(line => line[property])).filter(Boolean))].sort((a, b) => a.localeCompare(b, "ru"));
    byId(id).replaceChildren(new Option(label, ""), ...values.map(value => new Option(value, value)));
    byId(id).value = values.includes(selected) ? selected : "";
  }
  const selected = byId("district-filter").value;
  const districts = [...new Set(state.schools.map(school => school.districtCity).filter(Boolean))].sort((a, b) => a.localeCompare(b, "ru"));
  const first = element("option", "", "Все районы и города"); first.value = "";
  byId("district-filter").replaceChildren(first, ...districts.map(value => { const option = element("option", "", value); option.value = value; return option; }));
  byId("district-filter").value = districts.includes(selected) ? selected : "";
}

async function refresh() {
  if (!state.token || state.loading) return;
  if (Date.now() >= state.expires) { endSession("Срок сессии истёк. Войдите заново."); return; }
  const epoch = state.epoch;
  state.loading = true;
  byId("refresh").disabled = true;
  byId("refresh").setAttribute("aria-busy", "true");
  byId("updated-at").textContent = "Обновление…";
  try {
    const user = await request("/api/auth/me", { token: state.token, signal: state.controller.signal });
    const schools = await request("/api/schools", { token: state.token, signal: state.controller.signal });
    if (epoch !== state.epoch) return;
    state.user = user;
    state.schools = schools;
    state.loaded = true;
    state.loadedAt = new Date().toISOString();
    byId("user-name").textContent = user.displayName;
    byId("user-role").textContent = roles[user.role] || "Пользователь";
    byId("access-description").textContent = accessDescription(user);
    updateDistricts(); renderSummary(); renderSchools();
    directoryPanel.render(schools, user);
    message("dashboard-message", "");
    byId("updated-at").textContent = `Обновлено ${timestamp(state.loadedAt)}`;
    await overviewTools.refresh(schools);
    if (schoolPanel.isOpen()) await schoolPanel.refresh();
  } catch (error) {
    if (epoch !== state.epoch) return;
    if (error.status === 401) { endSession("Сессия завершена или доступ изменён. Войдите заново."); return; }
    const detail = error.status === 429 ? "Слишком много запросов. Повторите обновление через минуту." : error.status === 403 ? "Нет прав для просмотра данных." : "Не удалось обновить данные. Проверьте соединение и попробуйте снова.";
    message("dashboard-message", detail + (state.loaded ? ` Показаны данные, загруженные ${timestamp(state.loadedAt)}.` : ""));
    byId("updated-at").textContent = "Данные не обновлены";
    if (!state.loaded) byId("result-count").textContent = "Не удалось загрузить школы";
  } finally {
    if (epoch === state.epoch) {
      state.loading = false;
      byId("refresh").disabled = false;
      byId("refresh").setAttribute("aria-busy", "false");
    }
  }
}

const schoolPanel = createSchoolPanel({ request, getSession: () => ({ token: state.token, epoch: state.epoch }),
  onUnauthorized: () => endSession("Сессия завершена или доступ изменён. Войдите заново."), element, badge, lineCard });
const overviewTools = createOverviewTools({ request, getSession: () => ({ token: state.token, epoch: state.epoch }),
  onUnauthorized: () => endSession("Сессия завершена или доступ изменён. Войдите заново."), element });
const schoolMap = createSchoolMap({ element, openSchool: school => schoolPanel.open(school), request,
  getSession: () => ({ token: state.token, epoch: state.epoch }), onSaved: refresh,
  onUnauthorized: () => endSession("Сессия завершена или доступ изменён. Войдите заново.") });
const directoryPanel = createDirectoryPanel({ element, request,
  getSession: () => ({ token: state.token, epoch: state.epoch, user: state.user }), onSaved: refresh,
  onUnauthorized: () => endSession("Сессия завершена или доступ изменён. Войдите заново.") });
for (const [id, label] of [["provider-filter", "Поставщик"], ["connection-filter", "Тип подключения"]]) {
  const wrap = element("div", "filter"); const select = element("select"); select.id = id;
  const caption = element("label", "", label); caption.htmlFor = id;
  select.append(new Option("Все", "")); wrap.append(caption, select);
  byId("reset-filters").before(wrap);
}

byId("login-form").addEventListener("submit", async event => {
  event.preventDefault();
  const submit = byId("login-submit");
  if (submit.disabled) return;
  submit.disabled = true;
  submit.textContent = "Вход…";
  submit.setAttribute("aria-busy", "true");
  message("login-message", "");
  try {
    const result = await request("/api/auth/login", { method: "POST", body: { login: byId("login").value.trim(), password: byId("password").value } });
    const expires = Date.parse(result.expiresAtUtc);
    if (!result.accessToken || !result.user || !Number.isFinite(expires) || expires <= Date.now()) throw new Error("Invalid session");
    state.epoch++;
    state.controller = new AbortController();
    state.token = result.accessToken;
    state.user = result.user;
    state.expires = expires;
    byId("password").value = "";
    state.expiryTimer = setTimeout(() => endSession("Срок сессии истёк. Войдите заново."), expires - Date.now());
    showDashboard(result.user); renderSummary(); renderSchools();
    await refresh();
  } catch (error) {
    message("login-message", error.status === 401 ? "Неверный логин или пароль, либо вход временно ограничен. После пяти неверных попыток подождите 15 минут." :
      error.status === 429 ? "Слишком много попыток входа. Подождите минуту и попробуйте снова." : "Не удалось войти. Проверьте соединение и повторите попытку.");
  } finally { submit.disabled = false; submit.textContent = "Войти в систему →"; submit.setAttribute("aria-busy", "false"); }
});

byId("show-password").addEventListener("click", () => {
  const visible = byId("password").type === "password";
  byId("password").type = visible ? "text" : "password";
  byId("show-password").textContent = visible ? "Скрыть" : "Показать";
  byId("show-password").setAttribute("aria-label", visible ? "Скрыть пароль" : "Показать пароль");
  byId("show-password").setAttribute("aria-pressed", String(visible));
});

byId("logout").addEventListener("click", async () => {
  const token = state.token; const epoch = state.epoch + 1;
  endSession("Вы вышли из системы.");
  try { await request("/api/auth/logout", { method: "POST", token }); }
  catch (error) { if (epoch === state.epoch && error.status !== 401) message("login-message", "Данные в этой вкладке очищены, но сервер не подтвердил выход. Проверьте соединение."); }
});
byId("refresh").addEventListener("click", refresh);
for (const id of ["search", "district-filter", "status-filter", "provider-filter", "connection-filter"]) byId(id).addEventListener(id === "search" ? "input" : "change", renderSchools);
byId("reset-filters").addEventListener("click", () => { for (const id of ["search", "district-filter", "status-filter", "provider-filter", "connection-filter"]) byId(id).value = ""; renderSchools(); });
setInterval(() => { if (!document.hidden) refresh(); }, 60000);
document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });

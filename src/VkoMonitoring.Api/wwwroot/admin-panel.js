import { roleLabels, roleScope, userScopeLabel, auditPeriod } from "./admin-model.js";
import { timestamp } from "./dashboard-model.js";

export function createAdminPanel({ element, request, getSession, onUnauthorized }) {
  const host = document.getElementById("admin-panel");
  let schools = [], users = [], selectedUserId = "", generation = 0, busy = false, initialized = false;
  const details = element("details", "admin-editor");
  details.append(element("summary", "", "Администрирование пользователей, аудита и устройств"));
  const feedback = element("p", "section-note"); feedback.setAttribute("role", "status");
  const tabs = element("div", "admin-tabs");
  const panels = {};
  function tab(name, label) {
    const button = element("button", "button secondary", label); button.type = "button";
    button.addEventListener("click", () => showTab(name)); tabs.append(button);
    const panel = element("section", "admin-tab-panel"); panel.hidden = true; panels[name] = { button, panel }; return panel;
  }
  const usersPanel = tab("users", "Пользователи"), auditPanel = tab("audit", "Аудит"), devicesPanel = tab("devices", "Устройства"), settingsPanel = tab("settings", "Настройки мониторинга");
  details.append(element("p", "section-note", "Раздел доступен только администратору. Изменение роли, блокировка и сброс пароля немедленно отзывают действующие сессии пользователя."), tabs, feedback, usersPanel, auditPanel, devicesPanel, settingsPanel);
  host.append(details);

  const userForm = element("form", "admin-form"), userList = element("div", "table-scroll");
  const fields = {};
  function control(name, label, type = "text") {
    const wrap = element("label", "admin-field"), input = type === "select" ? element("select") : element("input");
    if (type !== "select") input.type = type; input.id = `admin-user-${name}`; fields[name] = input;
    wrap.append(element("span", "", label), input); userForm.append(wrap); return input;
  }
  const login = control("login", "Логин"), displayName = control("displayName", "Отображаемое имя"), password = control("password", "Новый пароль", "password"), role = control("role", "Роль", "select");
  login.required = displayName.required = role.required = true; login.maxLength = 64; displayName.maxLength = 200; password.minLength = 12; password.maxLength = 128;
  for (const [value, label] of Object.entries(roleLabels)) role.append(new Option(label, value));
  const school = control("schoolId", "Школа", "select"), district = control("districtCity", "Район / город"), provider = control("providerName", "Поставщик");
  const blockedWrap = element("label", "admin-check"), blocked = element("input"); blocked.type = "checkbox"; fields.isBlocked = blocked; blockedWrap.append(blocked, element("span", "", "Учётная запись заблокирована")); userForm.append(blockedWrap);
  const save = element("button", "button primary", "Создать пользователя"); save.type = "submit";
  const reset = element("button", "text-button", "Новый пользователь"); reset.type = "button";
  userForm.append(save, reset); usersPanel.append(element("h3", "", "Пользователи"), element("p", "section-note", "Пароль содержит 12–128 символов. Для существующего пользователя заполненный пароль будет установлен как новый."), userForm, userList);

  function updateScopeFields() {
    school.disabled = role.value !== "School" || busy; district.disabled = role.value !== "District" || busy; provider.disabled = role.value !== "Provider" || busy;
    school.required = role.value === "School"; district.required = role.value === "District"; provider.required = role.value === "Provider";
  }
  role.addEventListener("change", updateScopeFields);
  function resetUserForm(clearFeedback = true) {
    selectedUserId = ""; userForm.reset(); role.value = "School"; login.disabled = false; blocked.disabled = true; password.required = true; save.textContent = "Создать пользователя"; updateScopeFields(); if (clearFeedback) feedback.textContent = "";
  }
  reset.addEventListener("click", resetUserForm);
  function selectUser(user) {
    selectedUserId = user.userId; login.value = user.login; login.disabled = true; displayName.value = user.displayName; password.value = ""; password.required = false; role.value = user.role; school.value = user.schoolId || ""; district.value = user.districtCity || ""; provider.value = user.providerName || ""; blocked.checked = user.isBlocked; blocked.disabled = false; save.textContent = "Сохранить пользователя"; updateScopeFields(); userForm.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }
  function renderUsers() {
    const table = element("table"), head = element("thead"), body = element("tbody");
    const header = element("tr"); for (const text of ["Логин", "Имя", "Роль", "Область", "Статус", "Действие"]) { const th = element("th", "", text); th.scope = "col"; header.append(th); } head.append(header);
    for (const user of users) {
      const row = element("tr");
      for (const text of [user.login, user.displayName, roleLabels[user.role] || user.role, userScopeLabel(user, schools), user.isBlocked ? "Заблокирован" : "Активен"]) row.append(element("td", "", text));
      const action = element("td"), edit = element("button", "text-button", "Изменить"); edit.type = "button"; edit.addEventListener("click", () => selectUser(user)); action.append(edit); row.append(action); body.append(row);
    }
    const caption = element("caption", "visually-hidden", "Пользователи и области доступа"); table.append(caption, head, body); userList.replaceChildren(table);
  }
  async function loadUsers() {
    const session = getSession(), current = generation;
    try { users = await request("/api/users", { token: session.token }); if (current !== generation || session.epoch !== getSession().epoch) return; renderUsers(); }
    catch (error) { if (error.status === 401) onUnauthorized(); else feedback.textContent = "Не удалось загрузить пользователей."; }
  }
  userForm.addEventListener("submit", async event => {
    event.preventDefault(); if (busy) return; const session = getSession(), current = generation;
    try {
      setBusy(true); const scope = roleScope(role.value, { schoolId: school.value, districtCity: district.value, providerName: provider.value });
      if (selectedUserId) {
        await request(`/api/users/${selectedUserId}`, { method: "PUT", token: session.token, body: { displayName: displayName.value.trim(), role: role.value, ...scope, isBlocked: blocked.checked } });
        if (password.value) await request(`/api/users/${selectedUserId}/password`, { method: "POST", token: session.token, body: { newPassword: password.value } });
      } else {
        await request("/api/users", { method: "POST", token: session.token, body: { login: login.value.trim(), displayName: displayName.value.trim(), password: password.value, role: role.value, ...scope } });
      }
      if (current !== generation) return; const success = selectedUserId ? "Пользователь обновлён, его прежние сессии отозваны." : "Пользователь создан."; resetUserForm(false); feedback.textContent = success; await loadUsers();
    } catch (error) {
      if (error.status === 401) { onUnauthorized(); return; }
      feedback.textContent = error.detail || (error.status === 409 ? "Такой логин уже существует или нельзя заблокировать последнего администратора." : "Не удалось сохранить пользователя. Проверьте поля.");
    } finally { if (current === generation) setBusy(false); }
  });

  const auditForm = element("form", "admin-filters"), auditFrom = element("input"), auditTo = element("input"), auditActor = element("input"), auditAction = element("input"), auditList = element("div", "table-scroll");
  auditFrom.type = auditTo.type = "date"; auditActor.placeholder = "Пользователь"; auditAction.placeholder = "Действие или путь";
  function label(text, input) { const node = element("label", "admin-field"); node.append(element("span", "", text), input); return node; }
  const auditSubmit = element("button", "button secondary", "Показать аудит"); auditSubmit.type = "submit"; auditForm.append(label("С", auditFrom), label("По", auditTo), label("Пользователь", auditActor), label("Действие", auditAction), auditSubmit);
  const auditFeedback = element("p", "section-note"); auditFeedback.setAttribute("role", "status"); auditPanel.append(element("h3", "", "Журнал аудита"), auditForm, auditFeedback, auditList);
  const today = new Date(); auditTo.value = today.toISOString().slice(0, 10); auditFrom.value = new Date(today.getTime() - 30 * 86400000).toISOString().slice(0, 10);
  auditForm.addEventListener("submit", async event => {
    event.preventDefault(); const session = getSession(), current = generation;
    try {
      const period = auditPeriod(auditFrom.value, auditTo.value), query = new URLSearchParams({ ...period, limit: "1000" });
      let rows = await request(`/api/audit?${query}`, { token: session.token }); if (current !== generation) return;
      const actor = auditActor.value.trim().toLocaleLowerCase("ru"), action = auditAction.value.trim().toLocaleLowerCase("ru");
      rows = rows.filter(row => (!actor || row.actor.toLocaleLowerCase("ru").includes(actor)) && (!action || `${row.action} ${row.path}`.toLocaleLowerCase("ru").includes(action)));
      const table = element("table"), head = element("thead"), body = element("tbody"), header = element("tr");
      for (const text of ["Начало", "Пользователь", "Действие", "Путь", "HTTP", "IP"]) { const th = element("th", "", text); th.scope = "col"; header.append(th); } head.append(header);
      for (const row of rows) { const tr = element("tr"); for (const text of [timestamp(row.startedAtUtc), row.actor, row.action, row.path, row.statusCode ?? "—", row.clientIp || "—"]) tr.append(element("td", "", String(text))); body.append(tr); }
      table.append(element("caption", "visually-hidden", "Журнал административных действий"), head, body); auditList.replaceChildren(table); auditFeedback.textContent = `Показано событий: ${rows.length}.`;
    } catch (error) { if (error.status === 401) onUnauthorized(); else auditFeedback.textContent = error.message || "Не удалось загрузить аудит."; }
  });

  const deviceFeedback = element("p", "section-note"); deviceFeedback.setAttribute("role", "status"); const deviceList = element("div", "table-scroll");
  devicesPanel.append(element("h3", "", "Реестр устройств"), element("p", "section-note", "Новый токен показывается один раз. После ротации его нужно безопасно установить на соответствующий компьютер; старый токен сразу перестаёт работать."), deviceFeedback, deviceList);
  async function loadDevices() {
    const session = getSession(), current = generation; deviceFeedback.textContent = "Загрузка устройств…";
    try {
      const groups = await Promise.all(schools.map(async currentSchool => ({ school: currentSchool, devices: await request(`/api/schools/${currentSchool.schoolId}/devices`, { token: session.token }) })));
      if (current !== generation) return; const table = element("table"), head = element("thead"), body = element("tbody"), header = element("tr");
      for (const text of ["Школа", "Компьютер", "Линия", "Последняя связь", "Версия", "Статус", "Действия"]) { const th = element("th", "", text); th.scope = "col"; header.append(th); } head.append(header);
      for (const group of groups) for (const device of group.devices) {
        const tr = element("tr"); const line = group.school.lines.find(item => item.lineId === device.lineId);
        for (const text of [group.school.name, `${device.name}${device.room ? ` · ${device.room}` : ""}`, line?.name || device.lineId, timestamp(device.lastSeenAtUtc), device.agentVersion || "—", device.isBlocked ? "Заблокирован" : "Активен"]) tr.append(element("td", "", text));
        const actions = element("td"), block = element("button", "text-button", device.isBlocked ? "Разблокировать" : "Заблокировать"), rotate = element("button", "text-button", "Сменить токен"); block.type = rotate.type = "button";
        block.addEventListener("click", async () => mutateDevice(device, `/api/devices/${device.deviceId}/block-state`, { isBlocked: !device.isBlocked }, device.isBlocked ? "Устройство разблокировано." : "Устройство заблокировано."));
        rotate.addEventListener("click", async () => { if (!confirm(`Сменить токен устройства «${device.name}»? Старый токен перестанет работать.`)) return; const result = await mutateDevice(device, `/api/devices/${device.deviceId}/token/rotate`, {}, "Токен изменён.", true); if (result?.deviceToken) showOneTimeToken(result.deviceToken, device.name); });
        actions.append(block, rotate); tr.append(actions); body.append(tr);
      }
      table.append(element("caption", "visually-hidden", "Зарегистрированные устройства"), head, body); deviceList.replaceChildren(table); deviceFeedback.textContent = `Устройств: ${groups.reduce((sum, group) => sum + group.devices.length, 0)}.`;
    } catch (error) { if (error.status === 401) onUnauthorized(); else deviceFeedback.textContent = "Не удалось загрузить устройства."; }
  }
  async function mutateDevice(device, path, body, success, returnsValue = false) {
    const session = getSession(); try { const result = await request(path, { method: returnsValue ? "POST" : "PUT", token: session.token, body }); deviceFeedback.textContent = success; if (!returnsValue) await loadDevices(); return result; }
    catch (error) { if (error.status === 401) onUnauthorized(); else deviceFeedback.textContent = error.detail || "Не удалось изменить устройство."; return null; }
  }
  function showOneTimeToken(token, name) {
    const box = element("div", "activation-result"), input = element("input"); input.readOnly = true; input.value = token; input.setAttribute("aria-label", `Новый токен устройства ${name}`);
    const hide = element("button", "text-button", "Скрыть токен"); hide.type = "button"; hide.addEventListener("click", () => box.remove()); box.append(element("strong", "", `Новый токен: ${name}`), input, element("p", "section-note", "Скопируйте сейчас. После скрытия токен нельзя прочитать повторно."), hide); devicesPanel.insertBefore(box, deviceList);
  }

  const settingsForm = element("form", "admin-form"), settingsFeedback = element("p", "section-note"); settingsFeedback.setAttribute("role", "status");
  const settingInputs = {};
  function setting(name, label, step = "0.1") { const input = element("input"); input.type = "number"; input.min = "0.1"; input.step = step; input.required = true; settingInputs[name] = input; settingsForm.append(labelNode(label, input)); }
  function labelNode(text, input) { const node = element("label", "admin-field"); node.append(element("span", "", text), input); return node; }
  const windows = element("textarea"); windows.rows = 4; windows.required = true; windows.placeholder = "08:00-09:00\n12:00-13:00"; settingsForm.append(labelNode("Окна замеров, по одному в строке", windows));
  setting("minimumDownloadMbps", "Минимальный Download, Мбит/с"); setting("minimumUploadMbps", "Минимальный Upload, Мбит/с"); setting("maximumPingMilliseconds", "Максимальный Ping, мс", "1");
  setting("maximumJitterMilliseconds", "Максимальный Jitter, мс", "1"); setting("maximumPacketLossPercent", "Максимальные потери, %"); setting("minimumAvailabilityPercent", "Минимальная доступность, %");
  const saveSettings = element("button", "button primary", "Сохранить настройки"); saveSettings.type = "submit"; settingsForm.append(saveSettings);
  settingsPanel.append(element("h3", "", "Расписание и пороги"), element("p", "section-note", "Новые пороги применяются к следующим замерам. Агенты получают расписание при очередной служебной связи."), settingsForm, settingsFeedback);
  async function loadSettings() {
    const session = getSession(); settingsFeedback.textContent = "Загрузка настроек…";
    try { const value = await request("/api/settings/operations", { token: session.token }); windows.value = value.measurementWindows.join("\n"); for (const [name, input] of Object.entries(settingInputs)) input.value = value[name]; settingsFeedback.textContent = value.updatedAtUtc ? `Последнее изменение: ${timestamp(value.updatedAtUtc)}.` : "Используются настройки по умолчанию."; }
    catch (error) { if (error.status === 401) onUnauthorized(); else settingsFeedback.textContent = "Не удалось загрузить настройки."; }
  }
  settingsForm.addEventListener("submit", async event => {
    event.preventDefault(); const session = getSession(); saveSettings.disabled = true; settingsFeedback.textContent = "Сохраняем…";
    try { const body = { measurementWindows: windows.value.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean) }; for (const [name, input] of Object.entries(settingInputs)) body[name] = Number(input.value); const value = await request("/api/settings/operations", { method: "PUT", token: session.token, body }); settingsFeedback.textContent = `Настройки сохранены: ${timestamp(value.updatedAtUtc)}.`; }
    catch (error) { if (error.status === 401) onUnauthorized(); else settingsFeedback.textContent = error.detail || "Проверьте формат окон и значения порогов."; }
    finally { saveSettings.disabled = false; }
  });

  function setBusy(value) { busy = value; for (const input of userForm.querySelectorAll("input,select,button")) input.disabled = value; if (!value) { login.disabled = Boolean(selectedUserId); blocked.disabled = !selectedUserId; updateScopeFields(); } }
  function showTab(name) {
    for (const [key, value] of Object.entries(panels)) { value.panel.hidden = key !== name; value.button.classList.toggle("primary", key === name); value.button.classList.toggle("secondary", key !== name); }
    if (name === "audit") auditForm.requestSubmit(); if (name === "devices") loadDevices(); if (name === "settings") loadSettings();
  }
  function render(availableSchools, user) {
    const selectedSchool = school.value; schools = availableSchools; host.hidden = user?.role !== "Administrator"; if (host.hidden) return;
    school.replaceChildren(new Option("Выберите школу", ""), ...schools.map(item => new Option(item.name, item.schoolId)));
    school.value = schools.some(item => item.schoolId === selectedSchool) ? selectedSchool : "";
    if (!initialized) { initialized = true; details.open = false; resetUserForm(); showTab("users"); loadUsers(); }
  }
  function open() { if (host.hidden) return; details.open = true; host.scrollIntoView({ behavior: "smooth", block: "start" }); }
  function clear() { generation++; initialized = false; schools = []; users = []; host.hidden = true; details.open = false; feedback.textContent = ""; userList.replaceChildren(); auditList.replaceChildren(); deviceList.replaceChildren(); }
  return { render, open, clear };
}

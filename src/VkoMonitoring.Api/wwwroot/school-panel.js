import { metric, timestamp, presenceLabels } from "./dashboard-model.js";
import { measurements, historyLimit, periodRange, periodQuery, chartData, dateValue } from "./history-model.js";

export function createSchoolPanel({ request, getSession, onUnauthorized, onIncidents, element, badge, lineCard }) {
  const host = document.getElementById("school-detail");
  const overview = ["overview-header", "summary", "overview-tools", "schools-panel", "school-map-panel", "directory-panel", "incidents-panel"].map(id => document.getElementById(id));
  let schoolId = null, selectedDevice = "", version = 0, controller = null, overviewVisibility = [];
  let controls, content, feedback, updated, refreshButton;
  const button = (text, action, className = "button secondary") => {
    const node = element("button", className, text); node.type = "button";
    node.addEventListener("click", action); return node;
  };
  function notice(text) { feedback.textContent = text; feedback.hidden = !text; }
  function cancel() { version++; controller?.abort(); controller = null; }
  function clear() {
    cancel(); schoolId = null; selectedDevice = ""; host.replaceChildren(); host.hidden = true;
    host.setAttribute("aria-busy", "false");
    overviewVisibility.forEach(([node, hidden]) => node.hidden = hidden);
    overviewVisibility = [];
  }
  function close() {
    const id = schoolId; clear();
    document.title = "Школы и линии · Мониторинг интернета ВКО";
    document.getElementById(`open-${id}`)?.focus();
  }
  function open(school) {
    clear(); schoolId = school.schoolId;
    overviewVisibility = overview.map(node => [node, node.hidden]);
    overview.forEach(node => node.hidden = true); host.hidden = false;
    const header = element("header", "detail-header");
    const heading = element("div");
    const title = element("h1", "", school.name); title.tabIndex = -1;
    heading.append(button("← К списку школ", close, "text-button"), title,
      element("p", "page-subtitle", [school.districtCity, school.address].filter(Boolean).join(" · ") || "Адрес не указан"));
    refreshButton = button("↻ Обновить карточку", refresh);
    heading.append(button("Инциденты этой школы", () => { close(); onIncidents?.(school); }, "text-button"));
    updated = element("span", "updated-at", "Загрузка…");
    const actions = element("div", "refresh-area"); actions.append(refreshButton, updated); header.append(heading, actions);
    const period = element("div", "detail-period");
    controls = {};
    const presetLabel = element("label", "", "Период"); controls.preset = element("select"); controls.preset.id = "history-period"; presetLabel.htmlFor = controls.preset.id;
    for (const [value, text] of [["day", "Сегодня"], ["week", "7 дней"], ["month", "30 дней"], ["custom", "Свой период"]]) {
      const option = element("option", "", text); option.value = value; controls.preset.append(option);
    }
    controls.preset.value = "week";
    period.append(presetLabel, controls.preset);
    controls.dates = element("div", "detail-dates"); controls.dates.hidden = true;
    for (const [key, text] of [["from", "С"], ["to", "По"]]) {
      const label = element("label", "", text); const input = element("input");
      input.type = "date"; input.id = `history-${key}`; input.value = dateValue(new Date()); label.htmlFor = input.id;
      controls[key] = input; controls.dates.append(label, input);
    }
    controls.dates.append(button("Применить период", refresh)); period.append(controls.dates);
    period.append(element("span", "secondary-text", "Даты и время — в часовом поясе вашего устройства."));
    controls.preset.addEventListener("change", () => {
      controls.dates.hidden = controls.preset.value !== "custom";
      if (controls.preset.value !== "custom") refresh();
    });
    feedback = element("div", "message"); feedback.setAttribute("role", "alert"); feedback.hidden = true;
    content = element("div", "detail-content");
    host.append(header, period, feedback, content);
    document.title = `${school.name} · Мониторинг интернета ВКО`; title.focus();
    refresh();
  }
  function section(title, note) {
    const node = element("section", "detail-section"); node.append(element("h2", "", title));
    if (note) node.append(element("p", "section-note", note)); return node;
  }
  function analyticsView(data) {
    const node = section("Итоги за период", "Статистика всех доступных вам линий школы. История и графики ниже относятся к выбранному устройству.");
    node.append(element("p", "section-note", `Применённый период: ${historyTimestamp(data.fromUtc)} — ${historyTimestamp(new Date(Date.parse(data.toUtc) - 1).toISOString())}`));
    const grid = element("div", "analytics-grid");
    for (const [name, avg, min, max, unit] of [
      ["Download", data.averageDownloadMbps, data.minimumDownloadMbps, data.maximumDownloadMbps, "Мбит/с"],
      ["Upload", data.averageUploadMbps, data.minimumUploadMbps, data.maximumUploadMbps, "Мбит/с"],
      ["Ping", data.averagePingMilliseconds, data.minimumPingMilliseconds, data.maximumPingMilliseconds, "мс"]]) {
      const card = element("article", "summary-card");
      card.append(element("p", "summary-label", `${name} · среднее`), element("p", "analytics-value", `${metric(avg)} ${unit}`),
        element("p", "summary-note", `Мин. ${metric(min)} · макс. ${metric(max)}`)); grid.append(card);
    }
    const count = element("article", "summary-card");
    count.append(element("p", "summary-label", "Замеры"), element("p", "analytics-value", String(data.measurementCount)),
      element("p", "summary-note", data.measurementCount ? `С отклонениями: ${data.problemMeasurementCount} (${metric(data.problemMeasurementPercent)}%)` : "За период данных нет")); grid.append(count);
    node.append(grid, element("p", "section-note", data.measurementCount
      ? `Замеры без потери соединения: ${metric(data.availabilityPercent)}%. Это доля замеров, а не непрерывный uptime.`
      : "Доступность не рассчитана: за период нет замеров."));
    return node;
  }
  function devicesView(devices, school) {
    const node = section("Устройства", "Активность агента и качество интернета отображаются отдельно.");
    if (!devices.length) { node.append(element("p", "empty-description", "Нет доступных зарегистрированных устройств.")); return node; }
    const scroll = element("div", "table-scroll"), table = element("table"), head = element("thead"), header = element("tr"), body = element("tbody");
    for (const text of ["Устройство", "Линия", "Качество / агент", "Последняя связь", "Версия агента", "История"]) { const th = element("th", "", text); th.scope = "col"; header.append(th); }
    head.append(header);
    for (const device of devices) {
      const row = element("tr"), name = element("td"), status = element("td");
      name.append(element("strong", "", device.name), element("span", "secondary-text", device.room || "Помещение не указано"),
        element("span", "secondary-text", `Device ID: ${device.deviceId}`));
      const line = school.lines.find(item => item.lineId === device.lineId);
      const lineCell = element("td"); lineCell.append(element("span", "", line?.name || "Линия не указана"), element("span", "secondary-text", line?.providerName || "Поставщик не указан"));
      status.append(badge(device.status), element("span", "secondary-text", presenceLabels[device.agentPresence] || "Связь неизвестна"));
      const action = element("td"); action.append(button("Показать замеры", () => {
        selectedDevice = device.deviceId; refresh();
      }, "text-button"));
      row.append(name, lineCell, status, element("td", "", timestamp(device.lastSeenAtUtc)), element("td", "", device.agentVersion || "—"), action); body.append(row);
    }
    table.append(head, body); scroll.append(table); node.append(scroll); return node;
  }
  function hardwareView(data, devices) {
    const node = section("Оборудование", "Снимок собирается при запуске агента и повторно сохраняется только при изменении конфигурации.");
    if (!data) {
      node.append(element("p", "empty-description", devices.length
        ? "Агент ещё не передал инвентаризацию. Данные появятся после следующего запуска или перезапуска службы агента."
        : "Для этой школы пока нет зарегистрированных устройств."));
      return node;
    }
    const inventory = data.inventory, computer = inventory.computer || {}, os = inventory.operatingSystem || {}, memory = inventory.memory || {};
    const value = input => input === null || input === undefined || input === "" ? "—" : String(input);
    const size = bytes => bytes === null || bytes === undefined ? "—" : `${(Number(bytes) / 1073741824).toFixed(1)} ГБ`;
    const card = (title, rows) => {
      const item = element("article", "summary-card"); item.append(element("p", "summary-label", title));
      for (const [label, content] of rows) item.append(element("p", "section-note", `${label}: ${value(content)}`));
      return item;
    };
    const grid = element("div", "analytics-grid");
    grid.append(
      card("Компьютер", [["Имя", computer.hostName], ["Производитель", computer.manufacturer], ["Модель", computer.model], ["Серийный номер", computer.serialNumber]]),
      card("Операционная система", [["Windows", os.name], ["Версия", [os.version, os.build].filter(Boolean).join(" · ")], ["Архитектура", os.architecture]]),
      card("Процессор и память", [["CPU", inventory.processors?.map(cpu => cpu.name).filter(Boolean).join(", ")], ["Ядра / потоки", inventory.processors?.map(cpu => `${value(cpu.physicalCores)} / ${value(cpu.logicalProcessors)}`).join(", ")], ["RAM", size(memory.totalBytes)], ["Модулей", memory.modules?.length]]),
      card("BIOS и плата", [["Плата", [inventory.firmware?.baseboardManufacturer, inventory.firmware?.baseboardProduct].filter(Boolean).join(" · ")], ["BIOS", [inventory.firmware?.biosManufacturer, inventory.firmware?.biosVersion].filter(Boolean).join(" · ")]])
    );
    node.append(grid);
    const tableSection = (title, headers, rows) => {
      const wrapper = element("div", "table-scroll"), table = element("table"), head = element("thead"), tr = element("tr"), body = element("tbody");
      for (const header of headers) { const th = element("th", "", header); th.scope = "col"; tr.append(th); }
      head.append(tr);
      for (const row of rows) { const item = element("tr"); for (const cell of row) item.append(element("td", "", value(cell))); body.append(item); }
      table.append(head, body); wrapper.append(table); const part = section(title); part.append(wrapper); return part;
    };
    if (inventory.storage?.length) node.append(tableSection("Накопители", ["Модель", "Тип", "Объём", "Интерфейс", "Серийный номер"], inventory.storage.map(disk => [disk.model, disk.mediaType, size(disk.capacityBytes), disk.busType, disk.serialNumber])));
    if (inventory.graphics?.length) node.append(tableSection("Видеокарты", ["Название", "Память", "Драйвер"], inventory.graphics.map(gpu => [gpu.name, size(gpu.memoryBytes), gpu.driverVersion])));
    if (inventory.networkAdapters?.length) node.append(tableSection("Сетевые адаптеры", ["Адаптер", "Производитель", "MAC", "Тип", "Статус"], inventory.networkAdapters.map(adapter => [adapter.name, adapter.manufacturer, adapter.macAddress, adapter.adapterType, adapter.isEnabled ? "Включён" : "Отключён"])));
    node.append(element("p", "section-note", `Последнее обновление: ${timestamp(data.updatedAtUtc)}${data.changedAtUtc ? ` · конфигурация изменилась: ${timestamp(data.changedAtUtc)}` : ""}.`));
    if (data.sensitiveDetailsHidden) node.append(element("p", "section-note", "Серийные номера и MAC-адреса скрыты для роли школы."));
    return node;
  }
  function svg(tag, attrs = {}, text) {
    const node = document.createElementNS("http://www.w3.org/2000/svg", tag);
    for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
    if (text !== undefined) node.textContent = text; return node;
  }
  function historyTimestamp(value) {
    return new Intl.DateTimeFormat("ru-RU", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(new Date(value));
  }
  function chart(rows, key, name, unit) {
    const card = element("article", "chart-card"); card.append(element("h3", "", `${name} · ${unit}`));
    const data = chartData(rows, key);
    if (!data.points.length) { card.append(element("p", "section-note", "Нет числовых значений за период.")); return card; }
    const plot = svg("svg", { viewBox: "0 0 640 190", role: "img", "aria-label": `${name}: ${data.points.length} значений. Точные значения доступны в таблице истории.` });
    plot.append(svg("title", {}, `${name}. Разрывы означают отсутствие показателя или соединения. Между замерами непрерывный мониторинг не выполняется.`));
    const maximum = Math.max(data.maximum * 1.15, 1), duration = data.to - data.from;
    const x = point => duration ? 62 + (point.time - data.from) / duration * 550 : 337;
    const y = point => 147 - point.value / maximum * 120;
    for (let i = 0; i <= 3; i++) {
      const position = 147 - i * 40;
      plot.append(svg("line", { x1: 62, x2: 612, y1: position, y2: position, class: "chart-grid" }),
        svg("text", { x: 52, y: position + 4, "text-anchor": "end", class: "chart-label" }, metric(maximum * i / 3)));
    }
    for (const segment of data.segments) {
      plot.append(svg("path", { d: segment.map((point, index) => `${index ? "L" : "M"}${x(point).toFixed(2)},${y(point).toFixed(2)}`).join(" "), class: "chart-series" }));
      // Isolated values remain visible; larger series use paths to keep the page responsive.
      if (segment.length === 1 || data.points.length <= 100) for (const point of segment) {
        const dot = svg("circle", { cx: x(point), cy: y(point), r: 3, class: "chart-point" });
        dot.append(svg("title", {}, `${timestamp(new Date(point.time).toISOString())}: ${metric(point.value)} ${unit}`)); plot.append(dot);
      }
    }
    plot.append(svg("text", { x: 62, y: 178, class: "chart-label" }, timestamp(new Date(data.from).toISOString())),
      svg("text", { x: 612, y: 178, "text-anchor": "end", class: "chart-label" }, timestamp(new Date(data.to).toISOString())));
    card.append(plot); return card;
  }
  function historyView(rows, devices) {
    const node = section("История измерений", "Графики показывают отдельные замеры. Разрывы — отсутствующие показатели или потеря соединения; нулевые значения сохранены.");
    const label = element("label", "device-select-label", "Устройство для истории"); label.htmlFor = "history-device";
    const select = element("select", "history-device"); select.id = label.htmlFor;
    for (const device of devices) { const option = element("option", "", `${device.name} · ${device.deviceId}`); option.value = device.deviceId; select.append(option); }
    select.value = selectedDevice;
    select.addEventListener("change", () => { selectedDevice = select.value; refresh(); }); node.append(label, select);
    if (!devices.length || !rows.length) { node.append(element("p", "history-empty", "За выбранный период замеров нет.")); return node; }
    if (rows.length >= historyLimit) node.append(element("p", "message info", `Показаны последние ${historyLimit} замеров устройства. Для полной истории уменьшите период. Итоги школы рассчитаны по всему периоду.`));
    const charts = element("div", "charts-grid");
    for (const [key, name, unit] of measurements) charts.append(chart(rows, key, name, unit)); node.append(charts);
    node.append(element("p", "section-note", `Загружено замеров: ${rows.length}. Таблица показывает последние ${Math.min(rows.length, 200)}. «На связи» означает успешное измерение, качество оценивается отдельно.`));
    const scroll = element("div", "table-scroll"), table = element("table"), head = element("thead"), header = element("tr"), body = element("tbody");
    for (const text of ["Дата и время", ...measurements.map(item => `${item[1]}, ${item[2]}`), "Соединение"]) { const th = element("th", "", text); th.scope = "col"; header.append(th); } head.append(header);
    for (const row of rows.slice().sort((a, b) => Date.parse(b.measuredAtUtc) - Date.parse(a.measuredAtUtc)).slice(0, 200)) {
      const tr = element("tr"); tr.append(element("td", "", historyTimestamp(row.measuredAtUtc)));
      for (const [key] of measurements) tr.append(element("td", "", metric(row[key])));
      const status = element("td", "", { Online: "На связи", Offline: "Нет соединения", Degraded: "Сбой измерения" }[row.connectionStatus] || "Неизвестно");
      if (row.failureReason) status.append(element("span", "secondary-text", row.failureReason)); tr.append(status); body.append(tr);
    }
    table.append(head, body); scroll.append(table); node.append(scroll); return node;
  }
  async function refresh() {
    if (!schoolId) return;
    let range;
    try { range = periodRange(controls.preset.value, controls.from.value, controls.to.value); }
    catch (error) { cancel(); content.replaceChildren(); notice(error.message); refreshButton.disabled = false; host.setAttribute("aria-busy", "false"); updated.textContent = "Период не применён"; return; }
    cancel(); const currentVersion = version, session = getSession(), id = schoolId;
    if (!session.token) return;
    controller = new AbortController(); const options = { token: session.token, signal: controller.signal };
    const valid = () => currentVersion === version && session.epoch === getSession().epoch && schoolId === id;
    refreshButton.disabled = true; host.setAttribute("aria-busy", "true"); updated.textContent = "Загрузка…"; notice("");
    // Clear old-period data immediately: failed requests must not masquerade as the newly selected period.
    content.replaceChildren(element("p", "section-note", "Загрузка карточки и измерений…"));
    try {
      const query = periodQuery(range);
      const [school, devices, analytics] = await Promise.all([
        request(`/api/schools/${id}`, options), request(`/api/schools/${id}/devices`, options),
        request(`/api/analytics?schoolId=${encodeURIComponent(id)}&${query}`, options)
      ]);
      if (!valid()) return;
      if (!devices.some(device => device.deviceId === selectedDevice)) selectedDevice = devices.find(device => device.lineId === school.primaryLineId)?.deviceId || devices[0]?.deviceId || "";
      const rows = selectedDevice ? await request(`/api/devices/${selectedDevice}/measurements?${query}&limit=${historyLimit}`, options) : [];
      let hardware = null;
      if (selectedDevice) {
        try { hardware = await request(`/api/devices/${selectedDevice}/inventory`, options); }
        catch (error) { if (error.status !== 404) throw error; }
      }
      if (!valid()) return;
      const lines = section("Линии школы", "Показано текущее состояние доступных вам линий; выбор периода влияет на итоги и историю.");
      const grid = element("div", "line-grid"); grid.append(...school.lines.map(lineCard));
      if (!school.lines.length) grid.append(element("p", "section-note", "Линии не зарегистрированы.")); lines.append(grid);
      const contacts = section("Ответственный и контакты", "Контактные данные организации образования.");
      for (const [label, value] of [["Ответственный", school.responsibleName], ["Должность", school.responsiblePosition],
        ["Телефон", school.responsiblePhone], ["Электронная почта", school.responsibleEmail]])
        contacts.append(element("p", "section-note", `${label}: ${value || "не указан"}`));
      content.replaceChildren(contacts, lines, analyticsView(analytics), devicesView(devices, school), hardwareView(hardware, devices), historyView(rows, devices));
      updated.textContent = `Обновлено ${timestamp(new Date().toISOString())}`;
    } catch (error) {
      if (!valid()) return;
      controller.abort(); content.replaceChildren();
      if (error.status === 401) { onUnauthorized(); return; }
      notice(error.status === 404 || error.status === 403 ? "Школа недоступна для вашей учётной записи." : error.status === 429 ? "Слишком много запросов. Повторите через минуту." : "Не удалось загрузить карточку. Проверьте соединение и повторите обновление.");
      updated.textContent = "Данные не загружены";
    } finally { if (valid()) { refreshButton.disabled = false; host.setAttribute("aria-busy", "false"); } }
  }
  return { open, clear, refresh, isOpen: () => schoolId !== null };
}

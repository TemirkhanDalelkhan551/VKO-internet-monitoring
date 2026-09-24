import { splitLocations, mapColors, parseLocation } from "./map-model.js";
import { statuses, metric, timestamp, schoolMeasurementDescription, agentPresenceDescription, openIncidentDescription } from "./dashboard-model.js";

export function createSchoolMap({ element, openSchool, request, getSession, onUnauthorized, onSaved }) {
  const host = document.getElementById("school-map-panel");
  const header = element("div", "map-heading");
  const title = element("div"); title.append(element("p", "eyebrow", "География связи"), element("h2", "", "Карта школ ВКО"));
  const fit = element("button", "text-button", "Показать все точки"); fit.type = "button";
  header.append(title, fit);
  const note = element("p", "map-note", "На карте — школы с подтверждёнными координатами. Цвет показывает оценку основной линии; связь с агентом указана отдельно.");
  const count = element("p", "result-count"); count.setAttribute("aria-live", "polite");
  const canvas = element("div", "school-map"); canvas.setAttribute("aria-label", "Карта расположения школ");
  const loading = element("p", "map-note map-loading", "Загрузка карты…"); loading.setAttribute("role", "status");
  const error = element("p", "map-note"); error.setAttribute("role", "status"); error.hidden = true;
  const legend = element("div", "map-legend");
  for (const [status, [label]] of Object.entries(statuses)) {
    const item = element("span", ""); const dot = element("i", "legend-dot"); dot.style.backgroundColor = mapColors[status];
    item.append(dot, document.createTextNode(label)); legend.append(item);
  }
  const missing = element("div", "map-missing");
  const editor = element("details", "map-editor"); editor.hidden = true;
  editor.append(element("summary", "", "Указать или изменить координаты школы"));
  const form = element("form", "map-location-form");
  function field(label, control) { const wrap = element("label", ""); wrap.append(element("span", "", label), control); return wrap; }
  const select = element("select"); select.id = "location-school";
  const latitude = element("input"); latitude.id = "location-latitude"; latitude.type = "text"; latitude.inputMode = "decimal";
  const longitude = element("input"); longitude.id = "location-longitude"; longitude.type = "text"; longitude.inputMode = "decimal";
  const save = element("button", "button primary", "Сохранить координаты"); save.type = "submit";
  const notice = element("p", "map-note"); notice.setAttribute("role", "status");
  form.append(field("Школа", select), field("Широта", latitude), field("Долгота", longitude), save);
  editor.append(form, element("p", "map-note", "Введите точное положение школы. Чтобы убрать точку с карты, очистите оба поля и сохраните. Адреса автоматически не преобразуются в координаты."), notice);
  host.append(header, note, count, loading, canvas, error, legend, missing, editor);
  let map, markers, config, configPromise, schools = [], allSchools = [], generation = 0, rendered = false, saving = false;
  function fitPoints() {
    if (!map) return;
    if (schools.length) map.fitBounds(schools.map(school => [school.latitude, school.longitude]), { padding: [30, 30], maxZoom: 13 });
    else map.setView(config.center, config.zoom);
  }
  function fillCoordinates() {
    const school = allSchools.find(school => school.schoolId === select.value);
    latitude.value = school?.latitude ?? ""; longitude.value = school?.longitude ?? ""; notice.textContent = "";
  }
  select.addEventListener("change", fillCoordinates);
  fit.addEventListener("click", fitPoints);
  form.addEventListener("submit", async event => {
    event.preventDefault(); if (save.disabled || !select.value) return;
    const session = getSession();
    try {
      const location = parseLocation(latitude.value, longitude.value);
      saving = true; save.disabled = true; notice.textContent = "Сохраняем…";
      await request(`/api/schools/${select.value}/location`, { method: "PUT", body: location, token: session.token });
      if (session.epoch !== getSession().epoch) return;
      notice.textContent = "Координаты сохранены.";
      await onSaved();
    } catch (exception) {
      if (session.epoch !== getSession().epoch) return;
      if (exception.status === 401) { onUnauthorized(); return; }
      notice.textContent = exception.status ? "Не удалось сохранить координаты. Проверьте права и соединение." : exception.message;
    } finally { if (session.epoch === getSession().epoch) { saving = false; save.disabled = !allSchools.length; } }
  });
  function popup(school) {
    const content = element("div", "map-popup");
    content.append(element("h3", "", school.name), element("p", "", school.districtCity || "Район не указан"),
      element("p", "", school.address || "Адрес не указан"), element("p", "", (statuses[school.status] || statuses.Unknown)[0]),
      element("p", "", school.primaryLineId ? school.providerName || "Поставщик не указан" : "Нет доступной основной линии"));
    const table = element("table");
    for (const [label, value] of [["Download", `${metric(school.latestMeasurement?.downloadMbps)} Мбит/с`],
      ["Upload", `${metric(school.latestMeasurement?.uploadMbps)} Мбит/с`], ["Ping", `${metric(school.latestMeasurement?.pingMilliseconds)} мс`],
      ["Jitter", `${metric(school.latestMeasurement?.jitterMilliseconds)} мс`], ["Потери", `${metric(school.latestMeasurement?.packetLossPercent)} %`],
      ["Последний замер", timestamp(school.latestMeasurement?.measuredAtUtc)], ["Свежесть", schoolMeasurementDescription(school)],
      ["Агент", school.primaryLineId ? agentPresenceDescription(school) : "Нет данных основной линии"],
      ["Открытые инциденты", openIncidentDescription(school.openIncidentCount || 0)],
      ["Устройства на связи", `${school.activeDeviceCount} / ${school.deviceCount}`]]) {
      const row = element("tr"); row.append(element("th", "", label), element("td", "", value)); table.append(row);
    }
    content.append(table);
    const open = element("button", "text-button", "Открыть карточку →"); open.type = "button";
    open.addEventListener("click", () => openSchool(school)); content.append(open); return content;
  }
  async function render(filtered, available, user) {
    const currentGeneration = generation;
    loading.hidden = false; canvas.setAttribute("aria-busy", "true");
    allSchools = available;
    const locations = splitLocations(filtered); schools = locations.located;
    count.textContent = `На карте: ${schools.length}. Без координат: ${locations.missing.length}. Фильтры общие с таблицей школ.`;
    missing.replaceChildren();
    if (locations.missing.length) {
      missing.append(element("p", "map-note", "Эти школы остаются в таблице, но не показаны на карте:"));
      for (const school of locations.missing) { const open = element("button", "text-button", school.name); open.type = "button";
        open.addEventListener("click", () => openSchool(school)); missing.append(open); }
    }
    editor.hidden = !["Administrator", "Regional"].includes(user?.role);
    const previous = select.value;
    select.replaceChildren(...available.map(school => { const option = element("option", "", school.name); option.value = school.schoolId; return option; }));
    if (available.some(school => school.schoolId === previous)) select.value = previous; else fillCoordinates();
    save.disabled = saving || !available.length;
    try {
      configPromise ??= fetch("/map-config.json").then(response => { if (!response.ok) throw new Error("Map configuration unavailable"); return response.json(); });
      config = await configPromise;
      if (currentGeneration !== generation) return;
      if (!window.L) throw new Error("Map library unavailable");
      if (!map) {
        map = L.map(canvas, { scrollWheelZoom: false, maxZoom: config.maxZoom }).setView(config.center, config.zoom);
        markers = L.featureGroup().addTo(map);
        const tiles = L.tileLayer(config.tileUrl, { maxZoom: config.maxZoom, attribution: config.attribution }).addTo(map);
        const failedTiles = new Set();
        const tileKey = event => `${event.coords.z}/${event.coords.x}/${event.coords.y}`;
        const updateTileNotice = () => {
          error.hidden = failedTiles.size === 0;
          error.textContent = "Часть подложки карты недоступна. Точки и таблица школ остаются доступны.";
        };
        tiles.on("tileerror", event => { failedTiles.add(tileKey(event)); updateTileNotice(); });
        tiles.on("tileload", event => { failedTiles.delete(tileKey(event)); updateTileNotice(); });
        tiles.on("tileunload", event => { failedTiles.delete(tileKey(event)); updateTileNotice(); });
      }
      map.invalidateSize(); markers.clearLayers();
      for (const school of schools) {
        const dot = element("span"); dot.style.backgroundColor = mapColors[school.status] || mapColors.Unknown;
        const icon = L.divIcon({ className: "school-map-marker", html: dot, iconSize: [24, 24], iconAnchor: [12, 12] });
        const marker = L.marker([school.latitude, school.longitude], { icon, title: `${school.name}: ${(statuses[school.status] || statuses.Unknown)[0]}`, alt: school.name });
        marker.bindTooltip(element("span", "", school.name)).bindPopup(popup(school), { minWidth: 190, maxWidth: 300, maxHeight: 240 }).addTo(markers);
      }
      if (!rendered) { fitPoints(); rendered = true; }
      loading.hidden = true; canvas.setAttribute("aria-busy", "false");
    } catch {
      if (currentGeneration !== generation) return;
      configPromise = null; loading.hidden = true; canvas.setAttribute("aria-busy", "false"); error.hidden = false; error.textContent = "Не удалось загрузить карту. Школы доступны в таблице ниже.";
    }
  }
  function clear() {
    generation++; map?.remove(); map = null; markers = null; schools = []; allSchools = []; rendered = false; saving = false;
    missing.replaceChildren(); select.replaceChildren(); latitude.value = ""; longitude.value = ""; notice.textContent = "";
    count.textContent = ""; loading.hidden = true; canvas.setAttribute("aria-busy", "false"); error.hidden = true; editor.hidden = true;
  }
  return { render, clear };
}

import { dateValue, periodQuery, periodRange } from "./history-model.js";

export function createRatingPanel({ element, request, getSession, onUnauthorized }) {
  const host = document.getElementById("ratings-panel");
  let generation = 0, controller;
  const from = element("input"), to = element("input"), feedback = element("p", "section-note");
  from.type = to.type = "date";
  const form = element("form", "incident-filters"), schools = element("div", "rating-table"), lines = element("div", "rating-table"), formula = element("details", "rating-formula");
  function field(text, input) { const label = element("label", "directory-field"); label.append(element("span", "", text), input); form.append(label); }
  function defaults() { const end = new Date(), start = new Date(end); start.setDate(start.getDate() - 29); from.value = dateValue(start); to.value = dateValue(end); }
  defaults(); field("Начало периода", from); field("Конец периода", to);
  const apply = element("button", "button secondary", "Рассчитать рейтинг"); apply.type = "submit"; form.append(apply);
  formula.append(element("summary", "", "Как считается рейтинг"));
  function number(value) { return Number.isFinite(value) ? value.toLocaleString("ru-RU", { maximumFractionDigits: 1 }) : "—"; }
  function renderTable(target, title, rows) {
    target.replaceChildren(element("h3", "", title));
    if (!rows.length) { target.append(element("p", "section-note", "Нет доступных школ или линий.")); return; }
    const table = element("table"), head = document.createElement("thead"), body = document.createElement("tbody");
    head.innerHTML = "<tr><th>Место</th><th>Объект</th><th>Индекс</th><th>Проблемные замеры</th><th>Средние показатели</th><th>Свежесть / инциденты</th></tr>";
    for (const row of rows) {
      const tr = document.createElement("tr"), metrics = Object.fromEntries((row.metrics || []).map(metric => [metric.name, metric]));
      const item = row.lineName ? `${row.schoolName} · ${row.lineName}` : row.schoolName;
      tr.append(cell(String(row.rank)), cell(item, row.category), cell(row.score === null ? "Нет данных" : `${number(row.score)} / 100`, row.category),
        cell(row.problemMeasurementPercent === null ? "—" : `${number(row.problemMeasurementPercent)}% (${row.problemMeasurementCount}/${row.measurementCount})`),
        cell(`↓ ${number(metrics.Download?.value)} Мбит/с · ↑ ${number(metrics.Upload?.value)} Мбит/с\nPing ${number(metrics.Ping?.value)} мс · Jitter ${number(metrics.Jitter?.value)} мс · Loss ${number(metrics["Packet Loss"]?.value)}%`),
        cell(`${row.freshness === "Fresh" ? "Свежие" : row.freshness === "Stale" ? "Устарели" : "Нет данных"}\nИнцидентов: ${row.incidentCount}`));
      body.append(tr);
    }
    table.append(head, body); const scroll = element("div", "table-scroll"); scroll.append(table); target.append(scroll);
  }
  function cell(main, note) { const td = document.createElement("td"); td.append(element("strong", "", main)); if (note) td.append(element("span", "secondary-text", note)); return td; }
  async function refresh() {
    const session = getSession(), current = ++generation;
    if (!session.token) return; controller?.abort(); controller = new AbortController();
    let query; try { query = new URLSearchParams(periodQuery(periodRange("custom", from.value, to.value))); } catch (error) { feedback.textContent = error.message; return; }
    feedback.textContent = "Расчёт рейтинга…"; apply.disabled = true;
    try {
      const result = await request(`/api/ratings?${query}`, { token: session.token, signal: controller.signal });
      if (current !== generation || session.epoch !== getSession().epoch) return;
      formula.replaceChildren(element("summary", "", "Как считается рейтинг"), ...result.formula.map(item => element("p", "section-note", `${item.name}: ${item.rule} Вес: ${item.weight}.`)));
      renderTable(schools, "Рейтинг школ", result.schools || []); renderTable(lines, "Рейтинг линий", result.lines || []);
      feedback.textContent = `Период: ${from.value} — ${to.value}. Формула и все входные показатели показаны ниже.`;
    } catch (error) {
      if (error.status === 401) { onUnauthorized(); return; }
      feedback.textContent = error.status === 422 ? "Слишком много замеров: выберите более короткий период." : "Не удалось построить рейтинг. Проверьте соединение и повторите.";
    } finally { if (current === generation) apply.disabled = false; }
  }
  form.addEventListener("submit", event => { event.preventDefault(); refresh(); });
  host.append(element("h2", "", "Прозрачный рейтинг качества"), element("p", "section-note", "Индекс строится по измерениям и инцидентам за выбранный период. Больший индекс — стабильнее связь."), form, feedback, formula, schools, lines);
  return { open() { host.hidden = false; host.focus(); refresh(); }, clear() { generation++; controller?.abort(); host.hidden = true; schools.replaceChildren(); lines.replaceChildren(); } };
}

export const historyLimit = 1000;
export const measurements = [
  ["downloadMbps", "Download", "Мбит/с"], ["uploadMbps", "Upload", "Мбит/с"],
  ["pingMilliseconds", "Ping", "мс"], ["jitterMilliseconds", "Jitter", "мс"],
  ["packetLossPercent", "Потери пакетов", "%"]
];

export function dateValue(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function calendarDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new Error("Укажите обе даты периода.");
  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(year, month - 1, day);
  if (dateValue(date) !== value) throw new Error("Укажите корректную дату.");
  return date;
}

// Calendar days are interpreted in the viewer's local timezone; the end day is inclusive.
export function periodRange(preset, from, to, now = new Date()) {
  let start, end;
  if (preset === "custom") {
    start = calendarDate(from); end = calendarDate(to);
    if (start > end) throw new Error("Начало периода не может быть позже окончания.");
  } else {
    const days = { day: 1, week: 7, month: 30 }[preset];
    if (!days) throw new Error("Выберите период.");
    end = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    start = new Date(end); start.setDate(start.getDate() - days + 1);
  }
  end.setDate(end.getDate() + 1);
  return { from: start.toISOString(), to: end.toISOString() };
}

export function periodQuery(range) { return new URLSearchParams(range).toString(); }

export function chartData(rows, key) {
  const ordered = rows.filter(row => Number.isFinite(Date.parse(row.measuredAtUtc)))
    .slice().sort((a, b) => Date.parse(a.measuredAtUtc) - Date.parse(b.measuredAtUtc));
  const segments = []; let current = [];
  for (const row of ordered) {
    const value = row[key];
    if (row.connectionStatus === "Offline" || typeof value !== "number" || !Number.isFinite(value)) {
      if (current.length) segments.push(current); current = []; continue;
    }
    current.push({ time: Date.parse(row.measuredAtUtc), value });
  }
  if (current.length) segments.push(current);
  const points = segments.flat();
  return { segments, points, from: ordered.length ? Date.parse(ordered[0].measuredAtUtc) : null,
    to: ordered.length ? Date.parse(ordered.at(-1).measuredAtUtc) : null,
    maximum: points.length ? Math.max(...points.map(point => point.value)) : null };
}

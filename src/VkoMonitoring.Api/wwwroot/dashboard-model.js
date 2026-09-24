export const roles = { School: "Школа", District: "Район / город", Regional: "Областной уровень", Provider: "Поставщик", Administrator: "Администратор" };
export const statuses = { Normal: ["Норма", "normal"], Unstable: ["Нестабильно", "unstable"], Critical: ["Критично", "critical"], NoConnection: ["Нет соединения", "no-connection"], Unknown: ["Нет актуальной оценки", "unknown"] };
export const presenceLabels = { Active: "Агент на связи", Inactive: "Агент не на связи", NotSeen: "Агент ещё не подключался", Blocked: "Агент заблокирован" };
export const lineTypes = { Primary: "Основная", Backup: "Резервная", Disabled: "Отключена" };

export function accessDescription(user) {
  switch (user.role) {
    case "School": return "Данные вашей организации образования";
    case "District": return user.districtCity || "Ваш район / город";
    case "Provider": return user.providerName || "Линии вашего поставщика";
    default: return "Все организации образования ВКО";
  }
}

export function filterSchools(schools, query, district, status, provider = "", connection = "") {
  const search = query.trim().toLocaleLowerCase("ru");
  return schools.filter(school => (!district || school.districtCity === district) && (!status || school.status === status) &&
    ((!provider && !connection) || school.lines.some(line => (!provider || line.providerName === provider) && (!connection || line.connectionType === connection))) &&
    (!search || [school.name, school.districtCity, ...school.lines.map(line => line.providerName)]
      .filter(Boolean).some(value => value.toLocaleLowerCase("ru").includes(search))));
}

export function summarize(schools) {
  const lines = schools.flatMap(school => school.lines).filter(line => line.lineStatus !== "Disabled");
  return { schools: schools.length, devices: schools.reduce((sum, school) => sum + school.deviceCount, 0),
    activeDevices: schools.reduce((sum, school) => sum + school.activeDeviceCount, 0),
    problemLines: lines.filter(line => ["Unstable", "Critical", "NoConnection"].includes(line.status)).length,
    missingLines: lines.filter(line => line.measurementFreshness !== "Fresh").length };
}

export function freshnessDescription(item) {
  if (item.measurementFreshness === "Stale") return "Замер устарел";
  if (item.measurementFreshness === "Missing") return "Замеров ещё нет";
  return "Актуальный замер";
}

export function agentPresenceDescription(item, now = Date.now()) {
  const label = presenceLabels[item.agentPresence] || "Связь с агентом неизвестна";
  return item.lastSeenAtUtc
    ? `${label} · последняя связь ${measurementAge(item.lastSeenAtUtc, now)} (${timestamp(item.lastSeenAtUtc)})`
    : label;
}

export function openIncidentDescription(count) {
  if (!Number.isInteger(count) || count <= 0) return "Открытых инцидентов нет";
  const form = new Intl.PluralRules("ru").select(count);
  const label = { one: "инцидент", few: "инцидента", many: "инцидентов", other: "инцидента" }[form];
  return `Открыто ${count} ${label}`;
}

export function dashboardRefreshFailureMessage(error) {
  if (error?.status === 429) return "Сервер мониторинга ограничил частоту запросов. Состояние школ не обновлено.";
  if (error?.status === 403) return "У этой учётной записи нет прав для обновления данных.";
  if (error?.status && error.status < 500) return "Сервер мониторинга отклонил запрос. Состояние школ не обновлено.";
  return "Сервер мониторинга недоступен или вернул ошибку. Это не означает, что интернет в школе отключён; состояние линий сейчас не обновлено.";
}

export function isMonitoringServerUnavailable(error) {
  return !error?.status || error.status >= 500 || error.name === "AbortError";
}

export function measurementAge(value, now = Date.now()) {
  const measured = value ? new Date(value).getTime() : NaN;
  if (!Number.isFinite(measured) || !Number.isFinite(now)) return "замеров ещё нет";
  const seconds = Math.max(0, Math.floor((now - measured) / 1000));
  if (seconds < 60) return "только что";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} мин назад`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ч назад`;
  return `${Math.floor(hours / 24)} дн назад`;
}

export function schoolMeasurementDescription(school) {
  return school.primaryLineId ? freshnessDescription(school) : "Нет сводки основной линии";
}

export function lineCountLabel(count) {
  const form = new Intl.PluralRules("ru").select(count);
  return `${count} ${{ one: "линия", few: "линии", many: "линий", other: "линии" }[form]}`;
}

export function metric(value, digits = 1) {
  return typeof value === "number" && Number.isFinite(value) ?
    value.toLocaleString("ru-RU", { minimumFractionDigits: 0, maximumFractionDigits: digits }) : "—";
}

export function timestamp(value) {
  const date = value ? new Date(value) : null;
  return date && Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit"
  }).format(date) : "—";
}

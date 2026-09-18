export const incidentStatuses = { New: "Новый", SentToProvider: "Передан поставщику", InProgress: "В работе", WaitingForInformation: "Ожидает информации", Resolved: "Восстановлен", Closed: "Закрыт" };
const transitions = { New: ["SentToProvider"], SentToProvider: ["InProgress", "WaitingForInformation"], InProgress: ["WaitingForInformation", "Resolved"], WaitingForInformation: ["InProgress", "Resolved"], Resolved: ["Closed", "InProgress"], Closed: [] };
export function nextIncidentStatuses(status) { return transitions[status] || []; }
export function incidentPermissions(role) {
  const admin = ["Administrator", "Regional"].includes(role);
  return { create: admin || role === "School", assign: admin, status: admin || role === "Provider", comment: admin || role === "Provider" };
}
export function incidentDuration(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return "—";
  const minutes = Math.floor(seconds / 60);
  return `${Math.floor(minutes / 60)} ч ${minutes % 60} мин`;
}

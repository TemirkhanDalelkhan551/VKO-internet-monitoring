export const roleLabels = {
  Administrator: "Администратор",
  Regional: "Областной оператор",
  District: "Район / город",
  School: "Школа",
  Provider: "Поставщик"
};

export function roleScope(role, { schoolId = "", districtCity = "", providerName = "" } = {}) {
  return {
    schoolId: role === "School" ? schoolId || null : null,
    districtCity: role === "District" ? districtCity.trim() || null : null,
    providerName: role === "Provider" ? providerName.trim() || null : null
  };
}

export function userScopeLabel(user, schools = []) {
  if (user.role === "School") return schools.find(item => item.schoolId === user.schoolId)?.name || user.schoolId || "Школа не назначена";
  if (user.role === "District") return user.districtCity || "Район не назначен";
  if (user.role === "Provider") return user.providerName || "Поставщик не назначен";
  return "Все организации";
}

export function auditPeriod(from, to) {
  if (!from || !to) throw new Error("Укажите начало и конец периода.");
  const start = new Date(`${from}T00:00:00`), end = new Date(`${to}T23:59:59.999`);
  if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || start > end) throw new Error("Проверьте период аудита.");
  return { from: start.toISOString(), to: end.toISOString() };
}

import test from "node:test";
import assert from "node:assert/strict";
import { filterSchools, summarize, metric, freshnessDescription, measurementAge, schoolMeasurementDescription, timestamp, accessDescription, agentPresenceDescription, openIncidentDescription, dashboardRefreshFailureMessage, isMonitoringServerUnavailable } from "../../src/VkoMonitoring.Api/wwwroot/dashboard-model.js";

const primary = { providerName: "Main provider", lineStatus: "Primary", status: "Unknown", qualityStatus: "Normal", measurementFreshness: "Stale" };
const backup = { providerName: "Reserve provider", lineStatus: "Backup", status: "NoConnection", measurementFreshness: "Fresh" };
const school = { name: "Школа № 1", districtCity: "Усть-Каменогорск", status: "Unknown", deviceCount: 2, activeDeviceCount: 1, lines: [primary, backup] };

test("summary distinguishes stale historical health from a current failure on backup", () => {
  assert.deepEqual(summarize([school]), { schools: 1, devices: 2, activeDevices: 1, problemLines: 1, missingLines: 1 });
});
test("disabled lines do not inflate problem or missing measurement counts", () => {
  assert.equal(summarize([{ ...school, lines: [{ ...backup, lineStatus: "Disabled" }] }]).problemLines, 0);
  assert.equal(summarize([{ ...school, lines: [{ ...primary, lineStatus: "Disabled" }] }]).missingLines, 0);
});
test("search finds provider of backup without changing primary status", () => {
  assert.deepEqual(filterSchools([school], " RESERVE ", "", "Unknown"), [school]);
  assert.deepEqual(filterSchools([school], "reserve", "", "Normal"), []);
});
test("district, case-insensitive search and status filters combine", () => {
  assert.deepEqual(filterSchools([school], "шКоЛа", "Усть-Каменогорск", "Unknown"), [school]);
  assert.deepEqual(filterSchools([school], "", "Другой район", ""), []);
});
test("zero loss or throughput is not shown as a missing measurement", () => {
  assert.equal(metric(0), "0"); assert.equal(metric(null), "—"); assert.equal(metric(NaN), "—");
});
test("missing and stale measurements have separate explanations", () => {
  assert.equal(freshnessDescription(primary), "Замер устарел");
  assert.equal(freshnessDescription({ measurementFreshness: "Missing" }), "Замеров ещё нет");
});
test("empty scope yields empty list and zero summary", () => {
  assert.deepEqual(filterSchools([], "", "", ""), []);
  assert.equal(summarize([]).devices, 0); assert.equal(summarize([]).schools, 0);
});
test("invalid timestamp never appears as Invalid Date", () => {
  assert.equal(timestamp(null), "—"); assert.equal(timestamp("broken"), "—");
});
test("measurement age gives a direct, human-readable freshness cue", () => {
  assert.equal(measurementAge("2026-09-21T10:00:00Z", Date.parse("2026-09-21T10:03:00Z")), "3 мин назад");
  assert.equal(measurementAge(null, Date.now()), "замеров ещё нет");
});
test("provider and district see their own scope label", () => {
  assert.equal(accessDescription({ role: "Provider", providerName: "Own provider" }), "Own provider");
  assert.equal(accessDescription({ role: "District", districtCity: "Own district" }), "Own district");
});

test("agent heartbeat recency is stated separately from measurement age", () => {
  assert.match(agentPresenceDescription({ agentPresence: "Active", lastSeenAtUtc: "2026-09-21T10:00:00Z" }, Date.parse("2026-09-21T10:03:00Z")), /^Агент на связи · последняя связь 3 мин назад \(.+\)$/);
  assert.equal(agentPresenceDescription({ agentPresence: "NotSeen", lastSeenAtUtc: null }), "Агент ещё не подключался");
});

test("open incidents have a clear zero state and Russian plural forms", () => {
  assert.equal(openIncidentDescription(0), "Открытых инцидентов нет");
  assert.equal(openIncidentDescription(1), "Открыто 1 инцидент");
  assert.equal(openIncidentDescription(3), "Открыто 3 инцидента");
  assert.equal(openIncidentDescription(5), "Открыто 5 инцидентов");
});

test("API failures are described as monitoring-server problems, not school outages", () => {
  const message = dashboardRefreshFailureMessage({ status: 503 });
  assert.match(message, /Сервер мониторинга/);
  assert.match(message, /не означает, что интернет в школе отключён/);
  assert.match(dashboardRefreshFailureMessage(new TypeError("fetch failed")), /состояние линий сейчас не обновлено/);
  assert.match(dashboardRefreshFailureMessage({ status: 429 }), /ограничил частоту запросов/);
  assert.equal(isMonitoringServerUnavailable({ status: 503 }), true);
  assert.equal(isMonitoringServerUnavailable(new TypeError("fetch failed")), true);
  assert.equal(isMonitoringServerUnavailable({ status: 429 }), false);
  assert.equal(isMonitoringServerUnavailable({ status: 403 }), false);
});

test("unavailable primary summary does not claim that the school has no measurements", () => {
  assert.equal(schoolMeasurementDescription({ primaryLineId: null, measurementFreshness: "Missing", lines: [backup] }), "Нет сводки основной линии");
  assert.equal(schoolMeasurementDescription({ primaryLineId: "main", measurementFreshness: "Stale" }), "Замер устарел");
});

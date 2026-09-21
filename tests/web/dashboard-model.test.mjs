import test from "node:test";
import assert from "node:assert/strict";
import { filterSchools, summarize, metric, freshnessDescription, measurementAge, schoolMeasurementDescription, timestamp, accessDescription } from "../../src/VkoMonitoring.Api/wwwroot/dashboard-model.js";

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

test("unavailable primary summary does not claim that the school has no measurements", () => {
  assert.equal(schoolMeasurementDescription({ primaryLineId: null, measurementFreshness: "Missing", lines: [backup] }), "Нет сводки основной линии");
  assert.equal(schoolMeasurementDescription({ primaryLineId: "main", measurementFreshness: "Stale" }), "Замер устарел");
});

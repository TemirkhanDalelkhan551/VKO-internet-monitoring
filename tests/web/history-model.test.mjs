import test from "node:test";
import assert from "node:assert/strict";
import { periodRange, periodQuery, chartData, dateValue } from "../../src/VkoMonitoring.Api/wwwroot/history-model.js";

test("custom range includes the complete last local day, including month boundary", () => {
  const range = periodRange("custom", "2026-01-31", "2026-02-01");
  assert.equal(range.from, new Date(2026, 0, 31).toISOString());
  assert.equal(range.to, new Date(2026, 1, 2).toISOString());
});
test("presets cover 1, 7 and 30 calendar days across a year boundary", () => {
  const now = new Date(2026, 0, 2, 17);
  for (const [preset, start] of [["day", "2026-01-02"], ["week", "2025-12-27"], ["month", "2025-12-04"]]) {
    const range = periodRange(preset, null, null, now);
    assert.equal(dateValue(new Date(range.from)), start);
    assert.equal(dateValue(new Date(range.to)), "2026-01-03");
  }
});
test("invalid or reversed calendar dates are rejected instead of rolling over", () => {
  for (const [from, to] of [["", "2026-01-01"], ["2026-02-30", "2026-03-01"], ["2026-02-02", "2026-02-01"]])
    assert.throws(() => periodRange("custom", from, to));
  assert.throws(() => periodRange("unknown"));
  assert.doesNotThrow(() => periodRange("custom", "2024-02-29", "2024-02-29"));
});
test("query preserves UTC timestamps without interpreting plus signs", () => {
  const range = periodRange("custom", "2026-09-18", "2026-09-18");
  assert.deepEqual(Object.fromEntries(new URLSearchParams(periodQuery(range))), range);
});
test("charts sort chronologically, preserve zero, and break at missing/offline samples", () => {
  const rows = [
    { measuredAtUtc: "2026-09-18T04:00:00Z", downloadMbps: 5, connectionStatus: "Online" },
    { measuredAtUtc: "2026-09-18T01:00:00Z", downloadMbps: 0, connectionStatus: "Online" },
    { measuredAtUtc: "2026-09-18T02:00:00Z", downloadMbps: null, connectionStatus: "Degraded" },
    { measuredAtUtc: "2026-09-18T03:00:00Z", downloadMbps: 100, connectionStatus: "Offline" }
  ];
  const chart = chartData(rows, "downloadMbps");
  assert.deepEqual(chart.segments.map(segment => segment.map(point => point.value)), [[0], [5]]);
  assert.equal(chart.maximum, 5);
  assert.equal(rows[0].downloadMbps, 5);
});
test("empty and invalid metric data do not produce misleading zero charts", () => {
  assert.equal(chartData([], "downloadMbps").maximum, null);
  assert.deepEqual(chartData([{ measuredAtUtc: "invalid", downloadMbps: 10 }], "downloadMbps").points, []);
  assert.equal(chartData([{ measuredAtUtc: "2026-09-18", downloadMbps: NaN }], "downloadMbps").maximum, null);
});

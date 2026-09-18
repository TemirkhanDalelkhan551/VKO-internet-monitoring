import test from "node:test";
import assert from "node:assert/strict";
import { optionalSpeed, contractShortfalls } from "../../src/VkoMonitoring.Api/wwwroot/directory-model.js";

test("contract speed accepts decimal comma and empty optional values", () => {
  assert.equal(optionalSpeed(" 100,125 "), 100.125);
  assert.equal(optionalSpeed(" "), null);
  assert.equal(optionalSpeed("999999999.999"), 999999999.999);
  for (const value of ["0", "-1", "1e2", "1.0001", "1000000000", "NaN", "1,2,3"]) assert.throws(() => optionalSpeed(value));
});
test("contract comparison preserves zero and does not invent missing measurements", () => {
  const line = { measurementFreshness: "Fresh", lineStatus: "Primary", contractedDownloadMbps: 100, contractedUploadMbps: 50,
    latestMeasurement: { downloadMbps: 0, uploadMbps: null } };
  assert.deepEqual(contractShortfalls(line), ["Download"]);
  assert.deepEqual(contractShortfalls({ ...line, measurementFreshness: "Stale" }), []);
  assert.deepEqual(contractShortfalls({ ...line, lineStatus: "Disabled" }), []);
  assert.deepEqual(contractShortfalls({ ...line, latestMeasurement: { downloadMbps: 100, uploadMbps: 50 } }), []);
});

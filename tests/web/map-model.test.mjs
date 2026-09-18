import test from "node:test";
import assert from "node:assert/strict";
import { hasCoordinates, splitLocations, parseLocation } from "../../src/VkoMonitoring.Api/wwwroot/map-model.js";
import { filterSchools } from "../../src/VkoMonitoring.Api/wwwroot/dashboard-model.js";

test("missing coordinates never become a marker at zero", () => {
  const located = { latitude: 0, longitude: 0 };
  const missing = { latitude: null, longitude: null };
  assert.deepEqual(splitLocations([located, missing]), { located: [located], missing: [missing] });
  for (const latitude of [NaN, Infinity, 91, "49"]) assert.equal(hasCoordinates({ latitude, longitude: 82 }), false);
});
test("coordinate editor supports decimal commas and explicit removal", () => {
  assert.deepEqual(parseLocation("49,97", "82.61"), { latitude: 49.97, longitude: 82.61 });
  assert.deepEqual(parseLocation(" ", ""), { latitude: null, longitude: null });
  for (const pair of [["", "82"], ["91", "82"], ["49", "Infinity"], ["text", "82"]])
    assert.throws(() => parseLocation(...pair));
});
test("provider and connection filters must match the same accessible line", () => {
  const school = { name: "School", status: "Unknown", lines: [
    { providerName: "Main", connectionType: "Ethernet" }, { providerName: "Reserve", connectionType: "Wi-Fi" }] };
  assert.equal(filterSchools([school], "", "", "", "Main", "Wi-Fi").length, 0);
  assert.equal(filterSchools([school], "", "", "Unknown", "Reserve", "Wi-Fi").length, 1);
});

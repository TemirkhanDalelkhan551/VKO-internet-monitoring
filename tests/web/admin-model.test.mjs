import test from "node:test";
import assert from "node:assert/strict";
import { roleScope, userScopeLabel, auditPeriod } from "../../src/VkoMonitoring.Api/wwwroot/admin-model.js";

test("role scope sends only the binding required by the selected role", () => {
  const all = { schoolId: "school", districtCity: " District ", providerName: " Provider " };
  assert.deepEqual(roleScope("School", all), { schoolId: "school", districtCity: null, providerName: null });
  assert.deepEqual(roleScope("District", all), { schoolId: null, districtCity: "District", providerName: null });
  assert.deepEqual(roleScope("Provider", all), { schoolId: null, districtCity: null, providerName: "Provider" });
  assert.deepEqual(roleScope("Administrator", all), { schoolId: null, districtCity: null, providerName: null });
});

test("user scope resolves a school name without inventing missing bindings", () => {
  assert.equal(userScopeLabel({ role: "School", schoolId: "s1" }, [{ schoolId: "s1", name: "Школа 1" }]), "Школа 1");
  assert.equal(userScopeLabel({ role: "District", districtCity: "Риддер" }), "Риддер");
  assert.equal(userScopeLabel({ role: "Provider", providerName: "ISP" }), "ISP");
  assert.equal(userScopeLabel({ role: "Regional" }), "Все организации");
});

test("audit period includes the complete local end date and rejects reversal", () => {
  const range = auditPeriod("2026-09-01", "2026-09-19");
  assert.ok(range.from.endsWith("00:00:00.000Z") || range.from.includes("T"));
  assert.ok(Date.parse(range.to) > Date.parse(range.from));
  assert.throws(() => auditPeriod("2026-09-20", "2026-09-19"));
});

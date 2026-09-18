import test from "node:test";
import assert from "node:assert/strict";
import { incidentPermissions, nextIncidentStatuses, incidentDuration } from "../../src/VkoMonitoring.Api/wwwroot/incident-model.js";
test("incident controls follow server role permissions", () => {
  for (const role of ["Administrator", "Regional"]) assert.deepEqual(incidentPermissions(role), {create:true,assign:true,status:true,comment:true});
  assert.deepEqual(incidentPermissions("School"), {create:true,assign:false,status:false,comment:false});
  assert.deepEqual(incidentPermissions("Provider"), {create:false,assign:false,status:true,comment:true});
  for (const role of ["District", null]) assert.deepEqual(incidentPermissions(role), {create:false,assign:false,status:false,comment:false});
});
test("incident transitions match lifecycle including reopening restored incidents", () => {
  assert.deepEqual(nextIncidentStatuses("New"), ["SentToProvider"]);
  assert.deepEqual(nextIncidentStatuses("SentToProvider"), ["InProgress", "WaitingForInformation"]);
  assert.deepEqual(nextIncidentStatuses("InProgress"), ["WaitingForInformation", "Resolved"]);
  assert.deepEqual(nextIncidentStatuses("WaitingForInformation"), ["InProgress", "Resolved"]);
  assert.deepEqual(nextIncidentStatuses("Resolved"), ["Closed", "InProgress"]);
  assert.deepEqual(nextIncidentStatuses("Closed"), []);
  assert.deepEqual(nextIncidentStatuses("unknown"), []);
});
test("incident duration preserves zero and does not invent missing values", () => {
  assert.equal(incidentDuration(0), "0 ч 0 мин");
  assert.equal(incidentDuration(3660), "1 ч 1 мин");
  for (const value of [null, NaN, -1, Infinity]) assert.equal(incidentDuration(value), "—");
});

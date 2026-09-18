import test from "node:test";
import assert from "node:assert/strict";
import { activationLifetime } from "../../src/VkoMonitoring.Api/wwwroot/activation-model.js";
test("activation lifetime enforces API bounds and whole minutes", () => {
  for (const value of ["5", "30", "1440", " 60 "]) assert.equal(activationLifetime(value), Number(value));
  for (const value of ["", "4", "1441", "-5", "5.5", "5e1", "Infinity"]) assert.throws(() => activationLifetime(value));
});

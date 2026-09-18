export function optionalSpeed(value) {
  if (!value.trim()) return null;
  const normalized = value.trim().replace(",", ".");
  if (!/^\d+(\.\d{1,3})?$/.test(normalized)) throw new Error("Укажите положительную скорость, до трёх знаков после запятой.");
  const speed = Number(normalized);
  if (!Number.isFinite(speed) || speed <= 0 || speed > 999999999.999) throw new Error("Скорость вне допустимого диапазона.");
  return speed;
}

export function contractShortfalls(line) {
  if (line.measurementFreshness !== "Fresh" || line.lineStatus === "Disabled") return [];
  return [["Download", line.latestMeasurement?.downloadMbps, line.contractedDownloadMbps],
    ["Upload", line.latestMeasurement?.uploadMbps, line.contractedUploadMbps]]
    .filter(([, measured, contracted]) => typeof measured === "number" && Number.isFinite(measured) &&
      typeof contracted === "number" && Number.isFinite(contracted) && contracted > 0 && measured < contracted)
    .map(([name]) => name);
}

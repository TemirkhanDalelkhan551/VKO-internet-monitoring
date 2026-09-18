export function hasCoordinates(school) {
  return typeof school.latitude === "number" && Number.isFinite(school.latitude) && Math.abs(school.latitude) <= 90 &&
    typeof school.longitude === "number" && Number.isFinite(school.longitude) && Math.abs(school.longitude) <= 180;
}

export function splitLocations(schools) {
  return { located: schools.filter(hasCoordinates), missing: schools.filter(school => !hasCoordinates(school)) };
}

export const mapColors = { Normal: "#26805b", Unstable: "#bc850f", Critical: "#db552f", NoConnection: "#b52845", Unknown: "#73838b" };

export function parseLocation(latitude, longitude) {
  if (!latitude.trim() && !longitude.trim()) return { latitude: null, longitude: null };
  if (!latitude.trim() || !longitude.trim()) throw new Error("Введите обе координаты или оставьте оба поля пустыми.");
  const location = { latitude: Number(latitude.replace(",", ".")), longitude: Number(longitude.replace(",", ".")) };
  if (!hasCoordinates(location)) throw new Error("Широта должна быть от −90 до 90, долгота — от −180 до 180.");
  return location;
}

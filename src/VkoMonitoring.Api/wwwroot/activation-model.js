export function activationLifetime(value) {
  if (!/^\d+$/.test(value.trim())) throw new Error("Укажите целое число минут от 5 до 1440.");
  const minutes = Number(value);
  if (!Number.isInteger(minutes) || minutes < 5 || minutes > 1440) throw new Error("Срок действия должен быть от 5 до 1440 минут.");
  return minutes;
}

import * as crypto from "node:crypto";

export function createNonce(): string {
  return crypto.randomBytes(16).toString("base64").replace(/[^A-Za-z0-9]/g, "");
}

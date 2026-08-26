import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = resolve(
  repositoryRoot,
  "node_modules/@microsoft/signalr/dist/browser/signalr.min.js");
const destination = resolve(
  repositoryRoot,
  "src/Quizizzo.Web/wwwroot/vendor/signalr.min.js");

await mkdir(dirname(destination), { recursive: true });
await copyFile(source, destination);
console.log(`Copied SignalR browser client to ${destination}`);

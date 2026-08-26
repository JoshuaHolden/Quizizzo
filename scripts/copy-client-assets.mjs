import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const assets = [
  {
    name: "SignalR",
    source: "node_modules/@microsoft/signalr/dist/browser/signalr.min.js",
    destination: "src/Quizizzo.Web/wwwroot/vendor/signalr.min.js"
  },
  {
    name: "Phaser",
    source: "node_modules/phaser/dist/phaser.min.js",
    destination: "src/Quizizzo.Web/wwwroot/vendor/phaser.min.js"
  }
];

for (const asset of assets) {
  const source = resolve(repositoryRoot, asset.source);
  const destination = resolve(repositoryRoot, asset.destination);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(source, destination);
  console.log(`Copied ${asset.name} browser client to ${destination}`);
}

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
  },
  {
    name: "Fredoka 600",
    source: "node_modules/@fontsource/fredoka/files/fredoka-latin-600-normal.woff2",
    destination: "src/Quizizzo.Web/wwwroot/fonts/fredoka-600.woff2"
  },
  {
    name: "Fredoka 700",
    source: "node_modules/@fontsource/fredoka/files/fredoka-latin-700-normal.woff2",
    destination: "src/Quizizzo.Web/wwwroot/fonts/fredoka-700.woff2"
  },
  {
    name: "Nunito 600",
    source: "node_modules/@fontsource/nunito/files/nunito-latin-600-normal.woff2",
    destination: "src/Quizizzo.Web/wwwroot/fonts/nunito-600.woff2"
  },
  {
    name: "Nunito 800",
    source: "node_modules/@fontsource/nunito/files/nunito-latin-800-normal.woff2",
    destination: "src/Quizizzo.Web/wwwroot/fonts/nunito-800.woff2"
  }
];

for (const asset of assets) {
  const source = resolve(repositoryRoot, asset.source);
  const destination = resolve(repositoryRoot, asset.destination);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(source, destination);
  console.log(`Copied ${asset.name} browser client to ${destination}`);
}

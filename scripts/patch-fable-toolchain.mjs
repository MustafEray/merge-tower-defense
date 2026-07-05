// Workaround for a packaging bug in fable-standalone (used by
// fable-compiler-js, our dotnet-free test toolchain): the package ships a
// UMD bundle but declares `"type": "module"`, so under Node ESM it exposes
// no named exports and instead assigns `globalThis.__FABLE_STANDALONE__`.
// fable-compiler-js does `import { init } from "fable-standalone"`, which
// then fails at link time. This script rewrites that single import to read
// the UMD global. Runs on postinstall; idempotent.
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const appPath = require.resolve("fable-compiler-js/dist/app.min.js");

if (!existsSync(appPath)) {
  console.error("patch-fable-toolchain: fable-compiler-js not found, skipping");
  process.exit(0);
}

const broken = 'import{init as Ys}from"fable-standalone";';
const fixed =
  'import"fable-standalone";const Ys=globalThis.__FABLE_STANDALONE__.init;';

const source = readFileSync(appPath, "utf8");

if (source.includes(fixed)) {
  console.log("patch-fable-toolchain: already patched");
} else if (source.includes(broken)) {
  writeFileSync(appPath, source.replace(broken, fixed));
  console.log("patch-fable-toolchain: patched fable-compiler-js");
} else {
  console.error(
    "patch-fable-toolchain: expected import not found — fable-compiler-js layout changed?"
  );
  process.exit(1);
}

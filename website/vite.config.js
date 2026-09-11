import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";
export default defineConfig({
  base: process.env.GITHUB_ACTIONS ? '/GOG-Disc-Packager/' : '/',
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL("./index.html", import.meta.url)),
        generator: fileURLToPath(
          new URL("./generator/index.html", import.meta.url),
        ),
      },
    },
  },
});

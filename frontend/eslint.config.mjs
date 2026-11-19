import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    // Workout media is served by the authenticated API; next/image's optimizer fetches server-side without the session cookie.
    files: ["components/MediaModal.tsx", "components/WorkoutMediaGallery.tsx"],
    rules: { "@next/next/no-img-element": "off" },
  },
  {
    // The global 401 handler runs outside React, so it cannot use the router; a full reload to /login is intended.
    files: ["lib/api.ts"],
    rules: { "@next/next/no-location-assign-relative-destination": "off" },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;

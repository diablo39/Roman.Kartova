// @ts-check
import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import globals from "globals";

export default tseslint.config(
  {
    ignores: [
      "dist",
      ".vite",
      "node_modules",
      "src/generated",
      "src/components/ui", // shadcn primitives — keep their style untouched
      "src/hooks/use-mobile.ts", // shadcn-generated viewport hook
      "vitest.config.ts",
      "vite.config.ts",
      "scripts",
      "openapi-snapshot.json",
    ],
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: "module",
      globals: { ...globals.browser },
    },
    plugins: {
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    // Forbid direct fetch() outside the api / auth / test seams.
    files: ["src/**/*.{ts,tsx}"],
    ignores: [
      "src/features/**/api/**",
      "src/shared/auth/**",
      "src/test/**",
      "src/__smoke__/**",
    ],
    rules: {
      "no-restricted-globals": [
        "error",
        {
          name: "fetch",
          message: "Use the typed openapi-fetch client (features/*/api or shared/auth).",
        },
      ],
    },
  }
);

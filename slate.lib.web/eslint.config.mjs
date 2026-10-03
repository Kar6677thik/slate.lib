import { defineConfig, globalIgnores } from "eslint/config";
import next from "eslint-config-next/core-web-vitals";
import ts from "eslint-config-next/typescript";
export default defineConfig([...next,...ts,{rules:{"react-hooks/set-state-in-effect":"off","react-hooks/refs":"off","@next/next/no-img-element":"off"}},globalIgnores([".next/**","output/**","test-results/**","playwright-report/**","next-env.d.ts"])]);

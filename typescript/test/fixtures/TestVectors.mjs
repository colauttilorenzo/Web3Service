// The expected values shared with the .NET tests (shared/test-vectors.json at the repository root).
import { readFileSync } from 'node:fs';

export const vectors = JSON.parse(readFileSync(new URL('../../../shared/test-vectors.json', import.meta.url), 'utf8'));
